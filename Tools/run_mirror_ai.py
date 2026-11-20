"""Measure the selection mirrors against the pinned Baseline-v1, using the original duel evaluator."""

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


def run_worker(index, jobs, editor, frozen, candidate, report_dir, fixture_path, driver):
    worker = WORKSPACE / 'runner' / str(index)
    bench.prepare(worker, frozen, candidate_snapshot=candidate)
    bench.write(worker / 'Assets/Scripts/Editor/MirrorFixtureDriver.cs', driver)
    bench.write(report_dir / f'candidate-source-{index}.json', bench.source_manifest(worker))
    report = report_dir / f'matches-{index}.jsonl'
    log = report_dir / f'unity-{index}.log'
    temp = worker / 'Temp/benchmark-process'
    temp.mkdir(parents=True, exist_ok=True)
    env = dict(os.environ, YGO_MIRROR_REPORT=str(report), YGO_MIRROR_FIXTURES=str(fixture_path),
               YGO_MIRROR_INDEX=str(index), YGO_MIRROR_WORKERS=str(jobs), TEMP=str(temp), TMP=str(temp))
    command = [str(editor), '-batchmode', '-nographics', '-quit', '-job-worker-count', '2',
               '-projectPath', str(worker), '-logFile', str(log),
               '-executeMethod', 'YGOPortable.Editor.MirrorFixtureDriver.Run']
    proc = subprocess.Popen(command, env=env, creationflags=subprocess.CREATE_NO_WINDOW)
    print(f'Unity worker {index} PID {proc.pid}, log {log}', flush=True)
    if proc.wait() != 0 or not report.is_file():
        raise RuntimeError(f'Benchmark worker failed: {log}')
    return bench.read_replay_report(report)


def validate_pairs(fixtures, rows):
    expected_decks = {(row['id'], row['seed']): row['deck'] for row in fixtures}
    expected = {(fixture, seed, seat) for fixture, seed in expected_decks for seat in (0, 1)}
    if not bench.complete_fixture_set(rows, expected):
        raise ValueError('The mirror games are missing or duplicated.')
    initial_by_fixture = {}
    for row in rows:
        key = row['matchup'], row['seed']
        sides = row['sides']
        if [side['deck'] for side in sides] != [expected_decks[key]] * 2:
            raise ValueError(f'A game used a deck other than the requested mirror: {key}')
        opening = [side['initial'] for side in sides]
        if initial_by_fixture.setdefault(key, opening) != opening:
            raise ValueError(f'Opening hands changed on seat swap: {key}')


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
    starting_logs = log_bytes()
    if starting_logs > LOG_CAP:
        parser.error(f'Raw logs already exceed 5 GB: {starting_logs} bytes.')

    frozen = bench.pin_revision(bench.BASELINE_V1)
    candidate = bench.pin_revision(args.candidate_revision)
    deck_manifest = FIXTURES.read_bytes()
    fixtures = selection_fixtures(deck_manifest, frozen, args.deck, args.seeds)
    report = WORKSPACE / args.label
    report.mkdir(parents=True, exist_ok=False)
    fixture_path = report / 'fixtures.jsonl'
    bench.write(fixture_path, ''.join(json.dumps(row) + '\n' for row in fixtures).encode())
    driver = DRIVER.read_bytes()
    version = (frozen / 'ProjectSettings/ProjectVersion.txt').read_text().splitlines()[0].split()[-1]
    editor = Path(f'C:/Program Files/Unity/Hub/Editor/{version}/Editor/Unity.exe')
    if jobs == 1:
        rows = run_worker(0, jobs, editor, frozen, candidate, report, fixture_path, driver)
    else:
        with ThreadPoolExecutor(max_workers=jobs) as pool:
            futures = [pool.submit(run_worker, index, jobs, editor, frozen, candidate, report,
                                   fixture_path, driver) for index in range(jobs)]
            rows = [row for future in futures for row in future.result()]

    validate_pairs(fixtures, rows)
    manifests = [(report / f'candidate-source-{index}.json').read_bytes() for index in range(jobs)]
    if len(set(manifests)) != 1:
        raise ValueError('Workers ran different candidate or telemetry sources.')
    policies = bench.policy_hashes(candidate / 'Assets/Resources')
    summary = bench.summary_identity(bench.summarize(rows), frozen, manifests[0], policies)
    summary.update({
        'candidateRevision': json.loads((candidate / 'manifest.json').read_bytes())['gitHead'],
        'purpose': 'isolated timing' if args.timing else 'selection',
        'workers': jobs,
        'deckSplitSha256': hashlib.sha256(deck_manifest).hexdigest(),
        'fixtureSha256': hashlib.sha256(fixture_path.read_bytes()).hexdigest(),
        'driverSha256': hashlib.sha256(driver).hexdigest(),
        'rawLogBytesAtStart': starting_logs,
        'timingEligible': args.timing,
    })
    bench.write(report / 'summary.json', (json.dumps(summary, ensure_ascii=False, indent=2) + '\n').encode())
    print(f'Report: {report / "summary.json"}')
    if any(summary['outcomes'].get(key) for key in ('nativeErrors', 'unfinished')) or any(
        counts['retries'] for counts in summary['blunders'].values()
    ):
        raise SystemExit(1)


if __name__ == '__main__':
    main()
