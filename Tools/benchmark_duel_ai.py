"""Run paired AI duels in isolated Unity Editor projects on Windows."""

import argparse
from collections import Counter, defaultdict
from concurrent.futures import ThreadPoolExecutor
import hashlib
import json
import math
import os
from pathlib import Path
import re
import shutil
import subprocess


PROJECT = Path(__file__).resolve().parents[1]
POLICIES = ('DuelAiPolicy', 'LinkComboPolicy')
STREAMING = (
    'cards_kr.json', 'cards_prop.bin', 'cardscripts.pack', 'deck_effect_categories.bin',
    'duel_effect_labels_ko.tsv', 'opponents_db.json', 'opponent_deck_levels.json',
)
BLUNDERS = ('exposedAttackers', 'selfDestruction', 'lethalMisses', 'wastedHandtraps', 'retries')


def write(path, data):
    path.parent.mkdir(parents=True, exist_ok=True)
    if not path.exists() or path.read_bytes() != data:
        path.write_bytes(data)


def read_replay_report(path):
    source = path.read_text(encoding='utf-8-sig')
    try:
        return [json.loads(source)]
    except json.JSONDecodeError:
        return [json.loads(line) for line in source.splitlines() if line.strip()]


def inputs():
    return [*(f'Assets/StreamingAssets/{name}' for name in STREAMING),
            'Assets/Plugins/x86_64/ygoengine.dll', 'ProjectSettings/ProjectVersion.txt']


def freeze(directory):
    manifest = directory / 'manifest.json'
    if not manifest.exists():
        files = [*inputs(), *(f'Assets/Resources/{name}.json' for name in POLICIES)]
        files.extend(path.relative_to(PROJECT).as_posix() for path in (PROJECT / 'Assets/Scripts/AI').glob('*.cs'))
        hashes = {}
        for name in sorted(files):
            data = (PROJECT / name).read_bytes()
            write(directory / name, data)
            hashes[name] = hashlib.sha256(data).hexdigest()
        revision = subprocess.check_output(['git', 'rev-parse', 'HEAD'], cwd=PROJECT, text=True).strip()
        write(manifest, (json.dumps({'gitHead': revision, 'sha256': hashes}, indent=2) + '\n').encode())
    data = json.loads(manifest.read_bytes())
    for name, expected in data['sha256'].items():
        if hashlib.sha256((directory / name).read_bytes()).hexdigest() != expected:
            raise ValueError(f'Frozen benchmark input changed: {name}')
    for name in inputs():
        if hashlib.sha256((PROJECT / name).read_bytes()).hexdigest() != data['sha256'][name]:
            raise ValueError(f'Current engine or duel data differs from the fixed benchmark: {name}')


def prepare(worker, frozen, candidate_policies=None):
    for name in ('Assets/Scripts', 'Assets/Plugins/x86_64', 'Packages', 'ProjectSettings'):
        source = PROJECT / name
        shutil.copytree(source, worker / name, dirs_exist_ok=True, ignore=shutil.ignore_patterns('__pycache__'))
    for name in inputs():
        write(worker / name, (frozen / name).read_bytes())
    for name in POLICIES:
        path = Path('Assets/Resources') / (name + '.json')
        candidate = candidate_policies / path.name if candidate_policies is not None else PROJECT / path
        write(worker / path, candidate.read_bytes())
        write(worker / path.with_name('BenchmarkBaseline' + path.name), (frozen / path).read_bytes())
    for path in (frozen / 'Assets/Scripts/AI').glob('*.cs'):
        source = path.read_text(encoding='utf-8-sig').replace('namespace YGOPortable.AI', 'namespace YGOPortable.BenchmarkBaseline')
        for name in POLICIES:
            source = source.replace(f'ResourceName = "{name}"', f'ResourceName = "BenchmarkBaseline{name}"')
        write(worker / 'Assets/Scripts/BenchmarkBaseline' / path.name, source.encode())
    factory = '''using YGOPortable.BenchmarkBaseline;

namespace YGOPortable.Editor
{
    internal static class AiBenchmarkBaselineFactory
    {
        public static AiMatchBenchmark.Pilot Create(int level)
        {
            var ai = new TacticalAI(DuelAiPolicy.LoadIncluded(), LinkComboPolicy.LoadIncluded(),
                searchCombos: true, opponentLevel: level);
            return new AiMatchBenchmark.Pilot(ai.ComputeResponse, () => ai.SimulatedComboBranches);
        }
    }
}
'''
    if 'int opponentLevel' not in (frozen / 'Assets/Scripts/AI/TacticalAI.cs').read_text(encoding='utf-8-sig'):
        factory = factory.replace(', opponentLevel: level', '')
    write(worker / 'Assets/Scripts/Editor/AiBenchmarkBaselineFactory.cs', factory.encode())


def run_worker(index, arguments, editor, frozen, output):
    worker = arguments.workspace / f'worker-{index}'
    prepare(worker, frozen, arguments.candidate_policies)
    report = output / f'matches-{index}.jsonl'
    log = output / f'unity-{index}.log'
    env = dict(os.environ, YGO_BENCH_REPORT=str(report), YGO_BENCH_SEEDS=str(arguments.seeds),
               YGO_BENCH_START=str(index), YGO_BENCH_STRIDE=str(arguments.jobs),
               YGO_BENCH_STYLE_SUITE='1' if arguments.style_suite else '0',
               YGO_BENCH_ROSTER_LEVELS='1' if arguments.roster_levels else '0',
               YGO_BENCH_OPENINGS_ONLY='1' if arguments.openings_only else '0',
               YGO_BENCH_REFERENCE_ONLY='1' if arguments.reference_only else '0',
               YGO_BENCH_CAPTURE_REPLAYS='1' if arguments.capture_replays else '0',
               YGO_BENCH_FIXTURE=arguments.fixture or '',
               YGO_BENCH_REPLAY=str(output / 'replay-input.jsonl') if arguments.replay else '',
               YGO_BENCH_INSPECT=str(arguments.inspect_step),
               YGO_BENCH_TRY_RESPONSE=arguments.try_response or '',
               YGO_BENCH_AUDIT_BATTLE='1' if arguments.audit_battle else '0')
    proc = subprocess.Popen([str(editor), '-batchmode', '-nographics', '-job-worker-count', '2',
                             '-projectPath', str(worker), '-logFile', str(log), '-executeMethod',
                             'YGOPortable.Editor.AiMatchBenchmark.Run'], env=env, creationflags=subprocess.CREATE_NO_WINDOW)
    print(f'Unity worker {index} PID {proc.pid}, log {log}', flush=True)
    if proc.wait() != 0 or not report.is_file():
        raise RuntimeError(f'Benchmark worker failed: {log}')
    return [json.loads(line) for line in report.read_text(encoding='utf-8-sig').splitlines()]


def summarize(matches):
    outcomes = Counter()
    rates = defaultdict(lambda: [0, 0, 0, 0])
    blunders = {side: Counter() for side in ('baseline', 'candidate')}
    problems = {side: Counter() for side in blunders}
    examples = {side: {} for side in blunders}
    totals = {side: Counter() for side in blunders}
    exposure_costs = {side: Counter() for side in blunders}
    paired = Counter()
    for match in matches:
        candidate = match['candidate']
        winner = match['winner']
        if match.get('openingOnly'):
            if match['capped']:
                outcomes['unfinished'] += 1
        elif match['capped'] or winner < 0:
            outcomes['unfinished'] += 1
        else:
            reward = .5 if winner == 2 else int(winner == candidate)
            outcomes['draw' if winner == 2 else 'win' if reward == 1 else 'loss'] += 1
            paired[(match['matchup'], match['seed'])] += reward - .5
        outcomes['nativeErrors'] += match['nativeErrors']
        for seat, side in enumerate(match['sides']):
            role = 'candidate' if seat == candidate else 'baseline'
            for name in BLUNDERS:
                blunders[role][name] += side[name]
            for trace in side.get('exposureTraces', ()):
                damage = trace.get('damageTaken', 0)
                exposure_costs[role]['targeted'] += trace.get('targetedNextTurn', False)
                exposure_costs[role]['damaged'] += damage > 0
                exposure_costs[role]['lifeLost'] += damage
                exposure_costs[role]['duelsLost'] += trace.get('lostByBattle', False)
            for name in ('decisions', 'decisionMs', 'endTurns', 'boardTotal', 'openings', 'earlyBoard'):
                totals[role][name] += side[name]
            totals[role]['chosenAlternatives'] += side.get('chosenAlternatives', 0)
            totals[role]['slowestMs'] = max(totals[role]['slowestMs'], side['slowestMs'])
            index = 2 if seat == candidate else 0
            if side['reachedMain']:
                rates[match['era']][index] += int(side['aceReached'] or side['methodReached'])
                rates[match['era']][index + 1] += 1
                method = side.get('method', match['method'])
                if method:
                    key = f'method-{method:08x}'
                    rates[key][index] += int(side['methodReached'])
                    rates[key][index + 1] += 1
            for failure in side['failures']:
                kind = failure.split('\t', 1)[0]
                problems[role][kind] += 1
                examples[role].setdefault(kind, {
                    'matchup': match['matchup'], 'seed': match['seed'], 'deck': side['deck'],
                    'first': seat == 0, 'initial': side['initial'], 'earlyActions': side['earlyActions'], 'evidence': failure,
                })
    n = outcomes['win'] + outcomes['loss'] + outcomes['draw']
    wins = (outcomes['win'] + .5 * outcomes['draw']) / n if n else None
    positive = sum(pair > 0 for pair in paired.values())
    negative = sum(pair < 0 for pair in paired.values())
    informative = positive + negative
    p_value = sum(math.comb(informative, k) for k in range(positive, informative + 1)) / (2 ** informative)
    for data in totals.values():
        data['meanDecisionMs'] = data['decisionMs'] / max(1, data['decisions'])
        data['meanEndBoard'] = data['boardTotal'] / max(1, data['endTurns'])
    return {
        'games': len(matches), 'outcomes': outcomes, 'winRate': wins,
        'gainPercentagePoints': (wins - .5) * 100 if wins is not None else None,
        'pairedOneSidedP': p_value, 'opening': dict(rates), 'blunders': blunders, 'decisionAndBoard': totals,
        'exposureConsequences': exposure_costs,
        'topFailures': {role: [dict(kind=kind, count=count, example=examples[role][kind])
                               for kind, count in counts.most_common(5)] for role, counts in problems.items()},
    }


def summarize_roster(matches, output, jobs):
    expected = [(output / f'matches-{worker}.jsonl.roster').read_text().splitlines() for worker in range(jobs)]
    if any(roster != expected[0] for roster in expected):
        raise ValueError('Roster differs between benchmark workers.')
    deck_ids = [int(deck) for deck in expected[0]]
    fixtures = {row['matchup'] for row in matches}
    if len(matches) != len(deck_ids) or fixtures != set(range(len(deck_ids))):
        raise ValueError('Each opponent deck must be tested in both seats.')
    seen = [Counter(), Counter()]
    levels = defaultdict(Counter)
    results = Counter()
    for match in matches:
        results['nativeErrors'] += match['nativeErrors']
        results['incomplete'] += match['capped']
        for seat, side in enumerate(match['sides']):
            seen[seat][side['deck']] += 1
            level = levels[side['level']]
            level['openings'] += 1
            level['usedSearch'] += side['branches'] > 0
            level['alternatives'] += side['chosenAlternatives']
            for name in BLUNDERS:
                level[name] += side[name]
    if any(seat != Counter(deck_ids) for seat in seen) or 0 in levels:
        raise ValueError('An opponent deck or level was skipped.')
    return {'decks': len(deck_ids), 'games': len(matches), 'outcomes': results, 'levels': dict(sorted(levels.items()))}


def acceptance(before, after):
    if before['games'] != after['games'] or before['opening'].keys() != after['opening'].keys():
        raise ValueError('Acceptance requires the same fixed suite used for the baseline.')
    opening_gains = {}
    for category, scores in before['opening'].items():
        _, _, baseline_successes, baseline_total = scores
        _, _, successes, total = after['opening'][category]
        if not baseline_total or not total:
            raise ValueError(f'No comparable openings for {category}.')
        opening_gains[category] = 100 * (successes / total - baseline_successes / baseline_total)
    checks = {
        'winGainAtLeast8Points': before['winRate'] is not None and after['winRate'] is not None and
                                after['winRate'] >= .58 and after['winRate'] - before['winRate'] >= .08,
        'everyEraImproved': all(gain > 0 for name, gain in opening_gains.items() if not name.startswith('method-')),
        'noSummonMethodRegressed': all(gain >= 0 for name, gain in opening_gains.items() if name.startswith('method-')),
        'zeroCriticalBlunders': not any(after['blunders']['candidate'].values()),
        'noIncompleteMatchesOrNativeErrors': not any(after['outcomes'].get(key, 0) for key in ('unfinished', 'nativeErrors')),
        'averageDecisionAtMost300Ms': after['decisionAndBoard']['candidate']['meanDecisionMs'] <= 300,
    }
    return {'checks': checks, 'openingGainPercentagePoints': opening_gains, 'accepted': all(checks.values())}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('label', help='Unique name for this benchmark run in the workspace.')
    parser.add_argument('--workspace', type=Path, default=PROJECT / 'Logs/AiBenchmark')
    parser.add_argument('--unity', type=Path)
    parser.add_argument('--jobs', type=int, default=2)
    parser.add_argument('--seeds', type=int, default=8, help='Shuffle seeds per matchup, with two policy assignments each.')
    parser.add_argument('--fixture', help='Run a single MATCHUP:SEED pair or select the pair to replay.')
    parser.add_argument('--capture-replays', action='store_true', help='Keep exact engine responses for every match in the run.')
    parser.add_argument('--replay', type=Path, action='append', help='Replay recorded matches from a JSON or JSONL report. Repeat for additional files.')
    parser.add_argument('--replay-seat', type=int, choices=(0, 1), help='Select the candidate seat when a replay report has multiple matches.')
    parser.add_argument('--inspect-step', type=int, default=-1, help='Log the board and legal choices at this zero-based replay response.')
    parser.add_argument('--try-response', help='Test one alternative hex response, such as 03-00-00-00, at the inspected step.')
    parser.add_argument('--audit-battle', action='store_true', help='Check alternate winning battle lines at each recorded choice.')
    parser.add_argument('--reference-only', action='store_true', help='Measure the unchanged AI playing itself.')
    suite = parser.add_mutually_exclusive_group()
    suite.add_argument('--style-suite', action='store_true', help='Run added trap, lock, burn and alternate-win matchups.')
    suite.add_argument('--roster-levels', action='store_true', help='Check every available deck in both seats with the candidate AI through turn two.')
    parser.add_argument('--openings-only', action='store_true', help='Measure the first two duel turns without scoring match winners.')
    parser.add_argument('--candidate-policies', type=Path, help='Evaluate policies from an isolated training project.')
    parser.add_argument('--require-gates', action='store_true', help='Fail unless all improvement gates pass.')
    args = parser.parse_args()
    if not re.fullmatch(r'[\w-]+', args.label) or args.jobs < 1 or args.seeds < 1:
        parser.error('Use a simple run label and positive job/seed counts.')
    matchups = 5 if args.style_suite else 14
    if args.fixture:
        if not re.fullmatch(r'(0|[1-9][0-9]*):(0|[1-9][0-9]*)', args.fixture):
            parser.error('Use MATCHUP:SEED with zero-based indexes, for example 10:21.')
        matchup, seed = map(int, args.fixture.split(':'))
        if not args.replay and (matchup >= matchups or seed >= 2147483647):
            parser.error('Fixture matchup or seed lies outside the suite.')
        args.seeds = max(args.seeds, seed + 1)
    if args.inspect_step < -1:
        parser.error('Replay inspection needs a zero-based step index.')
    if (args.replay_seat is not None or args.inspect_step >= 0 or args.audit_battle) and not args.replay:
        parser.error('--replay-seat, --inspect-step and --audit-battle require --replay.')
    if args.try_response and (args.inspect_step < 0 or not re.fullmatch(r'(?:[0-9a-fA-F]{2}-)*[0-9a-fA-F]{2}', args.try_response)):
        parser.error('--try-response requires an inspection step and hyphen-separated hexadecimal bytes.')
    if args.require_gates and (args.fixture or args.replay):
        parser.error('Acceptance gates need a full benchmark suite.')
    if args.roster_levels and (args.fixture or args.replay):
        parser.error('Select a single fixture or use the full roster audit.')
    if args.fixture and not args.replay:
        args.jobs = 1
    if args.replay:
        matches = [match for path in args.replay for match in read_replay_report(path)]
        if any(not isinstance(match, dict) for match in matches):
            parser.error('Replay files must contain benchmark match objects.')
        selected = [match for match in matches if
                    (not args.fixture or (match['matchup'], match['seed']) == (matchup, seed)) and
                    (args.replay_seat is None or match['candidate'] == args.replay_seat)]
        if not selected:
            parser.error('No captured transcripts match this fixture and seat.')
        if any(not match.get('replay') for match in selected):
            parser.error('A selected match lacks a transcript. Capture all matches or select a recorded fixture and seat.')
        if args.inspect_step >= 0 and len(selected) != 1:
            parser.error('Inspection requires one match. Select it using --fixture and --replay-seat.')
        args.jobs = min(args.jobs, len(selected))
    args.workspace = args.workspace.resolve()
    if args.candidate_policies is not None:
        args.candidate_policies = args.candidate_policies.resolve()
    frozen = args.workspace / 'baseline'
    output = args.workspace / args.label
    output.mkdir(parents=True, exist_ok=False)
    if args.replay:
        write(output / 'replay-input.jsonl', ''.join(json.dumps(match, ensure_ascii=False) + '\n' for match in selected).encode())
    freeze(frozen)
    version = (PROJECT / 'ProjectSettings/ProjectVersion.txt').read_text().splitlines()[0].split()[-1]
    editor = args.unity or Path(f'C:/Program Files/Unity/Hub/Editor/{version}/Editor/Unity.exe')
    if not editor.is_file():
        raise FileNotFoundError(f'Unity Editor: {editor}')
    with ThreadPoolExecutor(max_workers=args.jobs) as pool:
        futures = [pool.submit(run_worker, i, args, editor, frozen, output) for i in range(args.jobs)]
        matches = [record for future in futures for record in future.result()]
    if args.replay:
        if len(matches) != len(selected):
            raise RuntimeError('Replay did not verify every requested match.')
        print(f'Verified {len(matches)} exact replay(s): {output}')
        return
    if args.roster_levels:
        summary = summarize_roster(matches, output, args.jobs)
        write(output / 'summary.json', (json.dumps(summary, ensure_ascii=False, indent=2) + '\n').encode())
        print(json.dumps(summary, indent=2))
        print(f'Report: {output / "summary.json"}')
        if args.require_gates or any(summary['outcomes'].values()) or any(level['retries'] for level in summary['levels'].values()):
            raise SystemExit(2)
        return
    expected = {(matchup, seed, seat) for seat in (0, 1)} if args.fixture else {
        (m, s, seat) for m in range(matchups) for s in range(args.seeds) for seat in (0, 1)}
    if len(matches) != len(expected) or {(r['matchup'], r['seed'], r['candidate']) for r in matches} != expected:
        raise RuntimeError('Benchmark result is missing fixed fixtures or contains duplicates.')
    summary = summarize(matches)
    summary['baselineManifestSha256'] = hashlib.sha256((frozen / 'manifest.json').read_bytes()).hexdigest()
    first_worker = args.workspace / 'worker-0'
    summary['candidatePolicySha256'] = {
        name: hashlib.sha256((first_worker / 'Assets/Resources' / f'{name}.json').read_bytes()).hexdigest()
        for name in POLICIES
    }
    report = args.workspace / 'before' / 'summary.json'
    if not args.fixture and not args.reference_only and not args.style_suite and not args.openings_only and report.exists():
        before = json.loads(report.read_bytes())
        if before['games'] == len(matches):
            summary['acceptance'] = acceptance(before, summary)
    write(output / 'summary.json', (json.dumps(summary, ensure_ascii=False, indent=2) + '\n').encode())
    print(json.dumps({key: summary[key] for key in ('games', 'outcomes', 'gainPercentagePoints', 'pairedOneSidedP', 'opening', 'blunders')}, indent=2))
    print(f'Report: {output / "summary.json"}')
    if args.require_gates and not summary.get('acceptance', {}).get('accepted', False):
        raise SystemExit(2)


if __name__ == '__main__':
    main()
