"""Run selection mirrors with the fixed evaluator and isolated AI policy versions."""

import argparse
from concurrent.futures import ThreadPoolExecutor
import hashlib
import json
import os
from pathlib import Path
import re
import subprocess

import benchmark_duel_ai as bench


TOOLS = Path(__file__).resolve().parent
WORKSPACE = bench.BENCHMARK_STORE / 'MirrorStage0'
FIXTURES = TOOLS / 'AiEvaluation/mirror-fixtures.json'
DRIVER = TOOLS / 'AiEvaluation/MirrorFixtureDriver.cs'
METHODS = {None: 0, 'fusion': 0x43000000, 'ritual': 0x45000000, 'synchro': 0x46000000,
           'xyz': 0x49000000, 'pendulum': 0x4A000000, 'link': 0x4C000000}
LOG_CAP = 5_000_000_000
EVALUATOR = '355e9050b54a3bcbf7698dd12bcea4d840dbab65'
BASELINE_ARM = 'baseline'
LEAGUE_ARMS = (BASELINE_ARM, 'previous-baseline', 'random', 'previous-random', 'direct')
EVALUATOR_PATHS = ('Assets/Scripts/Core', 'Assets/Scripts/Editor/AiMatchBenchmark.cs',
                   'Assets/Scripts/Editor/RandomLegalPilot.cs')


def selection_fixtures(raw, frozen, deck_ids, seed_count):
    suite = json.loads(raw)
    selection, holdout = suite['selection'], suite['holdout']
    ranged_seeds = [
        set(range(split['seedStart'], split['seedStart'] + split['seedCount']))
        for split in (selection, holdout)
    ]
    if ranged_seeds[0] & ranged_seeds[1]:
        raise ValueError('The selection and holdout seeds overlap.')
    source = frozen / 'Assets/StreamingAssets/opponents_db.json'
    decks = {deck['id']: deck for deck in json.loads(source.read_bytes())['decks']}
    signatures = []
    fixture_ids = set()
    for split in (selection, holdout):
        split_ids = {entry['deck'] for entry in split['decks']}
        ids = {entry['id'] for entry in split['decks']}
        if len(ids) != len(split['decks']) or len(split_ids) != len(split['decks']) or fixture_ids & ids:
            raise ValueError('The suite contains duplicate fixture or deck identifiers.')
        fixture_ids.update(ids)
        # Deck identity is the full Main and Extra Deck contents, including duplicate copies.
        signatures.append({
            (tuple(sorted(decks[deck]['main'])), tuple(sorted(decks[deck].get('extra') or ())))
            for deck in split_ids
        })
    if signatures[0] & signatures[1]:
        raise ValueError('The selection and holdout decks overlap.')
    if not 1 <= seed_count <= selection['seedCount']:
        raise ValueError('The requested seed count is outside the selection range.')
    selected = set(deck_ids or (entry['deck'] for entry in selection['decks']))
    available = {entry['deck'] for entry in selection['decks']}
    if not selected <= available:
        raise ValueError('The requested decks must be in the selection set.')
    return [
        dict(entry, seed=seed, method=METHODS[entry['method']])
        for entry in selection['decks'] if entry['deck'] in selected
        for seed in range(selection['seedStart'], selection['seedStart'] + seed_count)
    ]


def install_candidate(worker, snapshot):
    sources = snapshot / 'Assets/Scripts/AI'
    for path in sorted(sources.glob('*.cs')):
        source = path.read_text(encoding='utf-8-sig').replace(
            'namespace YGOPortable.AI', 'namespace YGOPortable.MirrorCandidate')
        for name in bench.POLICIES:
            source = source.replace(f'ResourceName = "{name}"', f'ResourceName = "MirrorCandidate{name}"')
        bench.write(worker / 'Assets/Scripts/MirrorCandidate' / path.name, source.encode())
    for name in bench.POLICIES:
        policy = snapshot / 'Assets/Resources' / f'{name}.json'
        bench.write(worker / 'Assets/Resources' / f'MirrorCandidate{name}.json', policy.read_bytes())
    level = ', opponentLevel: level' if 'int opponentLevel' in (sources / 'TacticalAI.cs').read_text(encoding='utf-8-sig') else ''
    factory = '''using YGOPortable.MirrorCandidate;

namespace YGOPortable.Editor
{
    internal static class AiBenchmarkMirrorCandidateFactory
    {
        public static AiMatchBenchmark.Pilot Create(int level)
        {
            var ai = new TacticalAI(DuelAiPolicy.LoadIncluded(), LinkComboPolicy.LoadIncluded(),
                searchCombos: trueLEVEL_ARGUMENT);
            return AiMatchBenchmark.TimedPilot(ai.ComputeResponse, ai.AdvanceComboPlanning,
                () => ai.SimulatedComboBranches, () => ai.ChosenComboAlternatives);
        }
    }
}
'''.replace('LEVEL_ARGUMENT', level)
    bench.write(worker / 'Assets/Scripts/Editor/AiBenchmarkMirrorCandidateFactory.cs', factory.encode())


def run_worker(index, jobs, editor, frozen, evaluator, candidate, previous, report_dir, fixture_path, driver):
    key = hashlib.sha256('|'.join(path.name if path else '' for path in (evaluator, candidate, previous)).encode())
    worker = WORKSPACE / 'runner' / key.hexdigest()[:12] / str(index)
    bench.prepare(worker, frozen, candidate_snapshot=evaluator, previous_snapshot=previous)
    install_candidate(worker, candidate)
    bench.write(worker / 'Assets/Scripts/Editor/MirrorFixtureDriver.cs', driver)
    bench.write(report_dir / f'candidate-source-{index}.json', bench.source_manifest(worker, candidate))
    bench.write(report_dir / f'evaluator-source-{index}.json', bench.source_manifest(worker))
    if previous:
        bench.write(report_dir / f'previous-source-{index}.json', bench.source_manifest(worker, previous))
    report = report_dir / f'matches-{index}.jsonl'
    log = report_dir / f'unity-{index}.log'
    temp = worker / 'Temp/benchmark-process'
    temp.mkdir(parents=True, exist_ok=True)
    env = dict(os.environ, YGO_MIRROR_REPORT=str(report), YGO_MIRROR_FIXTURES=str(fixture_path),
               YGO_MIRROR_INDEX=str(index), YGO_MIRROR_WORKERS=str(jobs),
               YGO_MIRROR_LEAGUE='1' if previous else '0', TEMP=str(temp), TMP=str(temp))
    command = [str(editor), '-batchmode', '-nographics', '-quit', '-job-worker-count', '2',
               '-projectPath', str(worker), '-logFile', str(log),
               '-executeMethod', 'YGOPortable.Editor.MirrorFixtureDriver.Run']
    proc = subprocess.Popen(command, env=env, creationflags=subprocess.CREATE_NO_WINDOW)
    print(f'Unity worker {index} PID {proc.pid}, log {log}', flush=True)
    if proc.wait() != 0:
        raise RuntimeError(f'Benchmark worker failed: {log}')
    if bench.policy_hashes(worker / 'Assets/Resources', 'MirrorCandidate') != bench.policy_hashes(candidate / 'Assets/Resources'):
        raise RuntimeError(f'Worker {index} used different candidate policies.')
    if previous and bench.policy_hashes(worker / 'Assets/Resources', 'BenchmarkPrevious') != bench.policy_hashes(previous / 'Assets/Resources'):
        raise RuntimeError(f'Worker {index} used different previous policies.')
    arms = LEAGUE_ARMS if previous else (BASELINE_ARM,)
    return {arm: bench.read_replay_report(report_dir / arm / report.name) for arm in arms}


def validate_pairs(fixtures, rows):
    scheduled = {(row['id'], row['seed']): row for row in fixtures}
    expected = {(fixture, seed, seat) for fixture, seed in scheduled for seat in (0, 1)}
    if not bench.complete_fixture_set(rows, expected):
        raise ValueError('The mirror games are missing or duplicated.')
    initial_by_fixture = {}
    for row in rows:
        key = row['matchup'], row['seed']
        sides = row['sides']
        fixture = scheduled[key]
        if ([side['deck'] for side in sides] != [fixture['deck']] * 2 or
                row['era'] != fixture['era'] or row['method'] != fixture['method']):
            raise ValueError(f'A game used a different mirror fixture: {key}')
        opening = [side['initial'] for side in sides]
        if initial_by_fixture.setdefault(key, opening) != opening:
            raise ValueError(f'Opening hands changed on seat swap: {key}')
    return initial_by_fixture


def consistent_manifest(report, kind, jobs):
    manifests = [(report / f'{kind}-source-{index}.json').read_bytes() for index in range(jobs)]
    if len(set(manifests)) != 1:
        raise ValueError(f'Workers ran different {kind} sources.')
    return manifests[0]


def clean(summary):
    return (not any(summary['outcomes'].get(key) for key in ('nativeErrors', 'unfinished')) and
            not any(counts['retries'] for counts in summary['blunders'].values()))


def verify_evaluator():
    # AI helpers are copied from EVALUATOR by prepare. The remaining host code comes
    # from the worktree, so check it before copying and evidence collection.
    git = ['git', '-C', str(bench.PROJECT)]
    status = subprocess.run([*git, 'diff', '--quiet', EVALUATOR, '--', *EVALUATOR_PATHS], check=False)
    untracked = subprocess.check_output(
        [*git, 'ls-files', '--others', '--exclude-standard', '-z', '--', EVALUATOR_PATHS[0]]
    ).split(b'\0')
    if status.returncode or any(path.endswith(b'.cs') for path in untracked):
        raise ValueError('The protected evaluator or protocol host differs from its fixed revision.')


def log_bytes():
    total = 0
    for folder, _, names in os.walk(bench.PROJECT / 'Logs'):
        for name in names:
            path = Path(folder) / name
            if path.suffix.lower() in {'.log', '.jsonl', '.stderr'}:
                try:
                    total += path.stat().st_size
                except FileNotFoundError:
                    continue
    return total


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('label')
    parser.add_argument('--candidate-revision', default='HEAD')
    parser.add_argument('--previous-revision',
                        help='Also measure direct mirrors and both policies against Baseline-v1 and RandomLegal-v1.')
    parser.add_argument('--seeds', type=int, default=6)
    parser.add_argument('--deck', type=int, action='append', help='Limit a smoke run to listed selection deck IDs.')
    parser.add_argument('--jobs', type=int, choices=(1, 2, 3, 4))
    parser.add_argument('--timing', action='store_true', help='Use one process to measure decision time.')
    args = parser.parse_args()
    if not re.fullmatch(r'[A-Za-z0-9][A-Za-z0-9_-]{0,63}', args.label) or args.label == 'runner':
        parser.error('Use a new run label containing letters, numbers, hyphens or underscores.')
    jobs = args.jobs if args.jobs is not None else 1 if args.timing else 2
    if (jobs == 1) != args.timing:
        parser.error('Use one worker for timing and two to four for win-rate trials.')
    verify_evaluator()
    starting_logs = log_bytes()
    if starting_logs > LOG_CAP:
        parser.error(f'Raw logs already exceed 5 GB: {starting_logs} bytes.')

    frozen = bench.pin_revision(bench.BASELINE_V1)
    evaluator = bench.pin_revision(EVALUATOR)
    candidate = bench.pin_revision(args.candidate_revision)
    previous = bench.pin_revision(args.previous_revision) if args.previous_revision else None
    deck_manifest = FIXTURES.read_bytes()
    fixtures = selection_fixtures(deck_manifest, frozen, args.deck, args.seeds)
    report = WORKSPACE / args.label
    report.mkdir(parents=True, exist_ok=False)
    fixture_path = report / 'fixtures.jsonl'
    bench.write(fixture_path, ''.join(json.dumps(row) + '\n' for row in fixtures).encode())
    driver = DRIVER.read_bytes()
    version = (frozen / 'ProjectSettings/ProjectVersion.txt').read_text().splitlines()[0].split()[-1]
    editor = Path(f'C:/Program Files/Unity/Hub/Editor/{version}/Editor/Unity.exe')
    worker_args = (jobs, editor, frozen, evaluator, candidate, previous, report, fixture_path, driver)
    if jobs == 1:
        batches = [run_worker(0, *worker_args)]
    else:
        with ThreadPoolExecutor(max_workers=jobs) as pool:
            futures = [pool.submit(run_worker, index, *worker_args) for index in range(jobs)]
            batches = [future.result() for future in futures]

    candidate_manifest = consistent_manifest(report, 'candidate', jobs)
    evaluator_manifest = consistent_manifest(report, 'evaluator', jobs)
    previous_manifest = consistent_manifest(report, 'previous', jobs) if previous else None
    candidate_revision = json.loads((candidate / 'manifest.json').read_bytes())['gitHead']
    previous_revision = json.loads((previous / 'manifest.json').read_bytes())['gitHead'] if previous else None
    metadata = {
        'evaluatorRevision': EVALUATOR,
        'evaluatorSourceManifestSha256': hashlib.sha256(evaluator_manifest).hexdigest(),
        'purpose': 'isolated timing' if args.timing else 'selection',
        'workers': jobs,
        'deckSplitSha256': hashlib.sha256(deck_manifest).hexdigest(),
        'fixtureSha256': hashlib.sha256(fixture_path.read_bytes()).hexdigest(),
        'driverSha256': hashlib.sha256(driver).hexdigest(),
        'rawLogBytesAtStart': starting_logs,
        'timingEligible': args.timing,
    }
    outcomes = {}
    opening = None
    for arm in batches[0]:
        rows = [row for batch in batches for row in batch[arm]]
        actual = validate_pairs(fixtures, rows)
        if opening is not None and opening != actual:
            raise ValueError(f'Opening hands changed across arms: {arm}')
        opening = actual
        is_previous = arm.startswith('previous-')
        policy_snapshot = previous if is_previous else candidate
        opponent = ('RandomLegal-v1' if arm.endswith('random') else
                    previous_revision if arm == 'direct' else 'Baseline-v1')
        summary = bench.summary_identity(
            bench.summarize(rows), frozen, previous_manifest if is_previous else candidate_manifest,
            bench.policy_hashes(policy_snapshot / 'Assets/Resources'), opponent)
        summary.update(metadata)
        summary['candidateRevision'] = previous_revision if is_previous else candidate_revision
        if arm.endswith('random'):
            win_rate = summary['outcomes'].get('win', 0) / len(rows)
            summary['randomFloor'] = {'winRate': win_rate, 'accepted': len(rows) >= 100 and win_rate >= .95 and clean(summary)}
        bench.write(report / arm / 'summary.json', (json.dumps(summary, ensure_ascii=False, indent=2) + '\n').encode())
        outcomes[arm] = summary

    summary = dict(outcomes[BASELINE_ARM])
    if previous:
        summary['league'] = {
            'previousRevision': previous_revision,
            'arms': {arm: {key: data[key] for key in ('opponent', 'candidateRevision', 'games', 'outcomes',
                                                        'winRate', 'pairedOneSidedP')}
                     for arm, data in outcomes.items()},
        }
    bench.write(report / 'summary.json', (json.dumps(summary, ensure_ascii=False, indent=2) + '\n').encode())
    print(f'Report: {report / "summary.json"}')
    if not all(clean(data) for data in outcomes.values()):
        raise SystemExit(1)


if __name__ == '__main__':
    main()
