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
import stat
import subprocess


PROJECT = Path(__file__).resolve().parents[1]
BENCHMARK_STORE = PROJECT / 'Logs/AiBenchmark'
BASELINE_V1 = '1da3213389f06a2689d9e0120d6aad8e673cf74d'
POLICIES = ('DuelAiPolicy', 'LinkComboPolicy')
STREAMING = (
    'cards_kr.json', 'cards_prop.bin', 'cardscripts.pack', 'deck_effect_categories.bin',
    'duel_effect_labels_ko.tsv', 'opponents_db.json', 'opponent_deck_levels.json',
)
GX_DECK_INPUTS = ('opponents_db.json', 'opponent_deck_levels.json', 'story_gx_rpg_decks.json')
BLUNDERS = ('exposedAttackers', 'selfDestruction', 'lethalMisses', 'wastedHandtraps', 'retries')
BLUNDER_RATE_KINDS = ('ownEffectDefeats', 'missedLethal', 'exposureDamageWithoutPayoff',
                      'wastedDisruption', 'resourcesLeftOnDefeat', 'downgradeSummons')
FIRST_TURN_DRAW_HANDTRAP_ACTIONS = frozenset(
    f'1/11:5:{code}' for code in (23434538, 42141493, 84192580, 87126721)
)


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


def pin_revision(revision):
    commit = subprocess.check_output(
        ['git', 'rev-parse', '--verify', '--end-of-options', f'{revision}^{{commit}}'], cwd=PROJECT, text=True
    ).strip()
    directory = BENCHMARK_STORE / ('Baseline-v1' if commit == BASELINE_V1 else f'Reference-{commit}')
    resources = [f'Assets/Resources/{name}.json' for name in POLICIES]
    tree = subprocess.check_output(['git', 'ls-tree', '-rz', commit, '--',
                                    'Assets/Scripts/AI', *inputs(), *resources], cwd=PROJECT)
    blobs = {}
    for entry in tree.rstrip(b'\0').split(b'\0'):
        metadata, name = entry.split(b'\t', 1)
        _, kind, blob = metadata.decode().split()
        name = name.decode()
        if kind == 'blob' and (not name.startswith('Assets/Scripts/AI/') or name.endswith('.cs')):
            blobs[name] = blob
    if not set([*inputs(), *resources]).issubset(blobs) or 'Assets/Scripts/AI/TacticalAI.cs' not in blobs:
        raise ValueError(f'Reference commit does not contain the required AI and duel data: {commit}')

    manifest = directory / 'manifest.json'
    recorded = json.loads(manifest.read_bytes()) if manifest.exists() else None
    if recorded is not None and (recorded.get('gitHead') != commit or recorded.get('gitBlobs') != blobs):
        raise ValueError(f'Pinned reference manifest changed: {manifest}')

    hashes = {}
    for name, blob in blobs.items():
        target = directory / name
        if not target.exists():
            if recorded is not None:
                raise ValueError(f'Pinned reference is missing {name}: {directory}')
            write(target, subprocess.check_output(['git', 'cat-file', 'blob', blob], cwd=PROJECT))
        source = target.read_bytes()
        digest = hashlib.new('sha1' if len(blob) == 40 else 'sha256')
        digest.update(f'blob {len(source)}\0'.encode())
        digest.update(source)
        if digest.hexdigest() != blob:
            raise ValueError(f'Pinned reference does not match its Git blob: {name}')
        hashes[name] = hashlib.sha256(source).hexdigest()

    if recorded is None:
        write(manifest, (json.dumps({'gitHead': commit, 'gitBlobs': blobs, 'sha256': hashes}, indent=2) + '\n').encode())
        if commit == BASELINE_V1:
            for target in (manifest, *(directory / name for name in blobs)):
                target.chmod(target.stat().st_mode & ~(stat.S_IWUSR | stat.S_IWGRP | stat.S_IWOTH))
    elif recorded.get('sha256') != hashes:
        raise ValueError(f'Pinned reference file hashes changed: {manifest}')
    return directory


def prepare(worker, frozen, candidate_policies=None, candidate_snapshot=None, previous_snapshot=None):
    for name in ('Assets/Scripts', 'Assets/Plugins/x86_64', 'Packages', 'ProjectSettings'):
        source = PROJECT / name
        ignore = shutil.ignore_patterns('__pycache__')
        if name == 'Assets/Scripts' and candidate_snapshot is not None:
            def ignore(path, names):
                return ['AI'] if Path(path) == source else []
        shutil.copytree(source, worker / name, dirs_exist_ok=True, ignore=ignore)
    if candidate_snapshot is not None:
        for path in (candidate_snapshot / 'Assets/Scripts/AI').glob('*.cs'):
            write(worker / 'Assets/Scripts/AI' / path.name, path.read_bytes())
    for name in inputs():
        write(worker / name, (frozen / name).read_bytes())
    for name in POLICIES:
        path = Path('Assets/Resources') / (name + '.json')
        candidate = candidate_policies / path.name if candidate_policies is not None else (candidate_snapshot or PROJECT) / path
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
    if previous_snapshot is not None:
        for path in (previous_snapshot / 'Assets/Scripts/AI').glob('*.cs'):
            source = path.read_text(encoding='utf-8-sig').replace(
                'namespace YGOPortable.AI', 'namespace YGOPortable.BenchmarkPrevious')
            for name in POLICIES:
                source = source.replace(f'ResourceName = "{name}"', f'ResourceName = "BenchmarkPrevious{name}"')
            write(worker / 'Assets/Scripts/BenchmarkPrevious' / path.name, source.encode())
        for name in POLICIES:
            resource = Path('Assets/Resources') / f'{name}.json'
            write(worker / resource.with_name('BenchmarkPrevious' + resource.name),
                  (previous_snapshot / resource).read_bytes())
        previous_factory = '''using YGOPortable.BenchmarkPrevious;

namespace YGOPortable.Editor
{
    internal static class AiBenchmarkPreviousFactory
    {
        public static AiMatchBenchmark.Pilot Create(int level)
        {
            var ai = new TacticalAI(DuelAiPolicy.LoadIncluded(), LinkComboPolicy.LoadIncluded(),
                searchCombos: true, opponentLevel: level);
            return AiMatchBenchmark.TimedPilot(ai.ComputeResponse, ai.AdvanceComboPlanning,
                () => ai.SimulatedComboBranches, () => ai.ChosenComboAlternatives);
        }
    }
}
'''
        if 'int opponentLevel' not in (previous_snapshot / 'Assets/Scripts/AI/TacticalAI.cs').read_text(encoding='utf-8-sig'):
            previous_factory = previous_factory.replace(', opponentLevel: level', '')
        write(worker / 'Assets/Scripts/Editor/AiBenchmarkPreviousFactory.cs', previous_factory.encode())


def source_manifest(project, candidate_snapshot=None):
    ai_source = candidate_snapshot or project
    paths = [
        *sorted((ai_source / 'Assets/Scripts/AI').glob('*.cs')),
        *sorted((project / 'Assets/Scripts/Core').glob('*.cs')),
        project / 'Assets/Scripts/Editor/AiMatchBenchmark.cs',
        *sorted((project / 'Assets/Scripts/Editor').glob('RandomLegalPilot.cs')),
    ]
    hashes = {(path.relative_to(ai_source) if path.parent == ai_source / 'Assets/Scripts/AI' else
               path.relative_to(project)).as_posix(): hashlib.sha256(path.read_bytes()).hexdigest() for path in paths}
    return (json.dumps(hashes, indent=2) + '\n').encode()


def policy_hashes(resource_directory, prefix=''):
    return {name: hashlib.sha256((resource_directory / f'{prefix}{name}.json').read_bytes()).hexdigest()
            for name in POLICIES}


def summary_identity(summary, frozen, manifest, policies, opponent=None):
    summary['baselineCommit'] = json.loads((frozen / 'manifest.json').read_bytes())['gitHead']
    summary['baselineName'] = frozen.name
    summary['opponent'] = opponent or frozen.name
    summary['baselineManifestSha256'] = hashlib.sha256((frozen / 'manifest.json').read_bytes()).hexdigest()
    summary['candidateSourceManifestSha256'] = hashlib.sha256(manifest).hexdigest()
    summary['candidatePolicySha256'] = policies
    return summary


def complete_fixture_set(matches, expected):
    return len(matches) == len(expected) and {
        (record['matchup'], record['seed'], record['candidate']) for record in matches} == expected


def verify_interleaved_inputs(previous, current):
    prior = {(record['matchup'], record['seed'], record['candidate']): record for record in previous}
    for record in current:
        key = (record['matchup'], record['seed'], record['candidate'])
        original = prior[key]
        if (original['era'] != record['era'] or original['method'] != record['method'] or
                original.get('openingOnly', False) != record.get('openingOnly', False) or
                [(side['deck'], side.get('level', 0), side['initial']) for side in original['sides']] !=
                [(side['deck'], side.get('level', 0), side['initial']) for side in record['sides']]):
            raise RuntimeError(f'Interleaved previous/candidate fixture inputs differ: {key}')
        prior_order, current_order = original.get('interleavedOrder'), record.get('interleavedOrder')
        if ({prior_order, current_order} != {1, 2} or
                current_order != (2 if sum(key) % 2 == 0 else 1)):
            raise RuntimeError(f'Interleaved pair does not have alternating 1/2 execution order: {key}')


def worker_path(workspace, frozen, index, candidate_snapshot=None, previous_snapshot=None):
    if previous_snapshot is not None:
        identity = json.dumps([str(candidate_snapshot) if candidate_snapshot else 'worktree',
                               str(previous_snapshot)], separators=(',', ':'))
        digest = hashlib.sha256(identity.encode()).hexdigest()[:16]
        return workspace / 'workers' / frozen.name / f'paired-{digest}' / f'w{index}'
    directory = workspace / 'workers' / frozen.name / (candidate_snapshot.name if candidate_snapshot else 'worktree')
    return directory / f'worker-{index}'


def run_worker(index, arguments, editor, frozen, output):
    worker = worker_path(arguments.workspace, frozen, index, arguments.candidate_snapshot, arguments.previous_snapshot)
    prepare(worker, frozen, arguments.candidate_policies, arguments.candidate_snapshot, arguments.previous_snapshot)
    if arguments.gx_suite:
        for name, contents in arguments.gx_deck_inputs.items():
            write(worker / 'Assets/StreamingAssets' / name, contents)
    write(output / f'candidate-source-{index}.json', source_manifest(worker))
    previous_report = None
    if arguments.previous_snapshot is not None:
        previous_report = (output / 'previous' / f'matches-{index}.jsonl').resolve()
        write(previous_report.with_name(f'candidate-source-{index}.json'),
              source_manifest(worker, arguments.previous_snapshot))
    report = output / f'matches-{index}.jsonl'
    log = output / f'unity-{index}.log'
    env = dict(os.environ, YGO_BENCH_REPORT=str(report), YGO_BENCH_SEEDS=str(arguments.seeds),
               YGO_BENCH_START=str(index), YGO_BENCH_STRIDE=str(arguments.jobs),
               YGO_BENCH_STYLE_SUITE='1' if arguments.style_suite else '0',
               YGO_BENCH_FULL_SUITE='1' if arguments.full_suite else '0',
               YGO_BENCH_GX_SUITE='1' if arguments.gx_suite else '0',
               YGO_BENCH_ROSTER_LEVELS='1' if arguments.roster_levels else '0',
               YGO_BENCH_OPENINGS_ONLY='1' if arguments.openings_only else '0',
               YGO_BENCH_REFERENCE_ONLY='1' if arguments.reference_only else '0',
               YGO_BENCH_RANDOM_OPPONENT='1' if arguments.random_opponent else '0',
               YGO_BENCH_CAPTURE_REPLAYS='1' if arguments.capture_replays or arguments.random_opponent or previous_report else '0',
               YGO_BENCH_FIXTURE=arguments.fixture or '',
               YGO_BENCH_REPLAY=str(output / 'replay-input.jsonl') if arguments.replay else '',
               YGO_BENCH_INSPECT=str(arguments.inspect_step),
               YGO_BENCH_TRY_RESPONSE=arguments.try_response or '',
               YGO_BENCH_AUDIT_BATTLE='1' if arguments.audit_battle else '0')
    env.pop('YGO_BENCH_PREVIOUS_REPORT', None)
    if previous_report is not None:
        env['YGO_BENCH_PREVIOUS_REPORT'] = str(previous_report)
    proc = subprocess.Popen([str(editor), '-batchmode', '-nographics', '-quit', '-job-worker-count', '2',
                             '-projectPath', str(worker), '-logFile', str(log), '-executeMethod',
                             'YGOPortable.Editor.AiMatchBenchmark.Run'], env=env, creationflags=subprocess.CREATE_NO_WINDOW)
    print(f'Unity worker {index} PID {proc.pid}, log {log}', flush=True)
    if proc.wait() != 0 or not report.is_file() or previous_report is not None and not previous_report.is_file():
        raise RuntimeError(f'Benchmark worker failed: {log}')
    return [json.loads(line) for line in report.read_text(encoding='utf-8-sig').splitlines()]


def run_fixed_regressions(arguments, editor, frozen, output):
    worker = worker_path(arguments.workspace, frozen, 0, arguments.candidate_snapshot)
    prepare(worker, frozen, arguments.candidate_policies, arguments.candidate_snapshot)
    fixture_dir = PROJECT / 'Tools/AiRegression'
    for fixture in fixture_dir.glob('*.json'):
        write(worker / 'Tools/AiRegression' / fixture.name, fixture.read_bytes())
    log = output / 'unity.log'
    env = dict(os.environ, YGO_AI_REGRESSION_OUT=str(output), YGO_AI_REGRESSION_CASES='')
    proc = subprocess.Popen([str(editor), '-batchmode', '-nographics', '-quit', '-job-worker-count', '2',
                             '-projectPath', str(worker), '-logFile', str(log), '-executeMethod',
                             'YGOPortable.Editor.AiFixedScenarioRegression.Run'], env=env, creationflags=subprocess.CREATE_NO_WINDOW)
    print(f'Fixed regression Unity PID {proc.pid}, log {log}', flush=True)
    code = proc.wait()
    print(f'Report: {output / "summary.json"}', flush=True)
    if code != 0 or not (output / 'summary.json').is_file():
        raise SystemExit(code or 1)


def verify_regression_report(directory, source, policy_hashes):
    report = json.loads((directory / 'summary.json').read_text(encoding='utf-8-sig'))
    fixtures_path = PROJECT / 'Tools/AiRegression/manifest.json'
    manifest = json.loads(fixtures_path.read_bytes())
    expected = {Path(name).stem for name in manifest['fixtures']} | set(manifest.get('builtInFixtures', []))
    cases = report.get('cases') or []
    evidence = {item['path']: item['sha256'].lower() for item in report.get('testedFiles') or []}
    required_files = {path: digest for path, digest in source.items() if path.startswith('Assets/Scripts/AI/') or
                      path in ('Assets/Scripts/Core/DuelSession.cs', 'Assets/Scripts/Core/CardDatabase.cs',
                               'Assets/Scripts/Core/DeckRepository.cs', 'Assets/Scripts/Core/YgoConstants.cs',
                               'Assets/Scripts/Core/YgoNativeBridge.cs')}
    required_files.update({f'Assets/Resources/{name}.json': value for name, value in policy_hashes.items()})
    return (report.get('complete') is True and report.get('passed') == len(expected) and report.get('failed') == 0 and
            len(cases) == len(expected) and {case['id'] for case in cases} == expected and
            all(case['passed'] and not case['retries'] and not case['scriptErrors'] for case in cases) and
            report.get('fixtureManifestSha256', '').lower() == hashlib.sha256(fixtures_path.read_bytes()).hexdigest() and
            all(evidence.get(path) == digest for path, digest in required_files.items()))


def summarize(matches):
    outcomes = Counter()
    rates = defaultdict(lambda: [0, 0, 0, 0])
    blunders = {side: Counter() for side in ('baseline', 'candidate')}
    problems = {side: Counter() for side in blunders}
    examples = {side: {} for side in blunders}
    totals = {side: Counter() for side in blunders}
    exposure_costs = {side: Counter() for side in blunders}
    destruction_breakdown = {side: Counter() for side in blunders}
    self_effect_defeats = dict.fromkeys(blunders, 0)
    rate_counts = {role: Counter(dict.fromkeys(BLUNDER_RATE_KINDS, 0)) for role in blunders}
    rate_coverage = True
    first_turn_draw_handtraps = dict.fromkeys(blunders, 0)
    styles = defaultdict(lambda: {side: Counter() for side in blunders})
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
            if winner == 1 - seat and side.get('selfDefeatEffect', 0):
                self_effect_defeats[role] += 1
                rate_counts[role]['ownEffectDefeats'] += 1
            rate_counts[role]['missedLethal'] += side['lethalMisses']
            rate_counts[role]['wastedDisruption'] += side['wastedHandtraps']
            for key, field in (('resourcesLeftOnDefeat', 'unusedDevelopmentEndTurnsOnLoss'),
                               ('downgradeSummons', 'linkMaterialDowngrades')):
                rate_coverage &= field in side
                rate_counts[role][key] += side.get(field, 0)
            first_turn_draw_handtraps[role] += sum(
                action in FIRST_TURN_DRAW_HANDTRAP_ACTIONS for action in side['earlyActions']
            )
            for name in ('equipRuleLosses', 'selfDestructionWithoutEquipRuleLoss'):
                if name in side:
                    destruction_breakdown[role][name] += side[name]
            for trace in side.get('exposureTraces', ()):
                damage = trace.get('damageTaken', 0)
                exposure_costs[role]['targeted'] += trace.get('targetedNextTurn', False)
                exposure_costs[role]['damaged'] += damage > 0
                exposure_costs[role]['lifeLost'] += damage
                exposure_costs[role]['duelsLost'] += trace.get('lostByBattle', False)
                rate_counts[role]['exposureDamageWithoutPayoff'] += bool(
                    damage > 0 and trace.get('alternative') and not trace.get('battlePayoff') and
                    not trace.get('fieldEffectResolved'))
            for name in ('decisions', 'decisionMs', 'endTurns', 'boardTotal', 'openings', 'earlyBoard'):
                totals[role][name] += side[name]
            style = styles[(side.get('style') or 'Unknown', 'first' if seat == 0 else 'second')][role]
            if not match.get('openingOnly') and not match['capped'] and winner >= 0:
                style['games'] += 1
                style['winPoints'] += .5 if winner == 2 else int(winner == seat)
            style['openingSuccesses'] += int(side['reachedMain'] and (side['aceReached'] or side['methodReached']))
            style['openingAttempts'] += int(side['reachedMain'])
            style['endTurns'] += side['endTurns']
            style['boardTotal'] += side['boardTotal']
            totals[role]['chosenAlternatives'] += side.get('chosenAlternatives', 0)
            totals[role]['slowestMs'] = max(totals[role]['slowestMs'], side['slowestMs'])
            totals[role]['maxPlanMs'] = max(totals[role]['maxPlanMs'], side.get('maxPlanMs', 0))
            totals[role]['ordinaryDecisionMs'] += side.get('ordinaryDecisionMs', side['decisionMs'])
            totals[role]['slowestOrdinaryMs'] = max(totals[role]['slowestOrdinaryMs'],
                                                   side.get('slowestOrdinaryMs', side['slowestMs']))
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
        data['meanOrdinaryDecisionMs'] = data['ordinaryDecisionMs'] / max(1, data['decisions'])
        data['meanEndBoard'] = data['boardTotal'] / max(1, data['endTurns'])
    style_report = defaultdict(dict)
    for (name, turn_order), policies in styles.items():
        for data in policies.values():
            data['winRate'] = data['winPoints'] / data['games'] if data['games'] else None
            data['meanEndBoard'] = data['boardTotal'] / data['endTurns'] if data['endTurns'] else None
        style_report[name][turn_order] = policies
    gx_fields = ('signatureMonsterReached', 'completedTurns', 'emptyFieldEndTurns', 'emptyMonsterEndTurns',
                 'battleFinishes', 'provenMissedBattleWins', 'lethalMisses', 'threatsSpent', 'wastedHandtraps',
                 'trackedResponses', 'selfDestruction', 'decisionMs', 'retries')
    gx_metrics = {role: Counter() for role in blunders}
    for match in matches:
        if not match['era'].startswith('GX/'):
            continue
        for seat, side in enumerate(match['sides']):
            role = 'candidate' if seat == match['candidate'] else 'baseline'
            gx_metrics[role]['games'] += 1
            for field in gx_fields:
                gx_metrics[role][field] += side[field]
    for counts in gx_metrics.values():
        if not counts:
            continue
        counts['decisionMsPerCompletedTurn'] = counts['decisionMs'] / max(1, counts['completedTurns'])
    return {
        'games': len(matches), 'outcomes': outcomes, 'winRate': wins,
        'gainPercentagePoints': (wins - .5) * 100 if wins is not None else None,
        'pairedOneSidedP': p_value, 'opening': dict(rates), 'blunders': blunders, 'decisionAndBoard': totals,
        'exposureConsequences': exposure_costs,
        'destructionBreakdown': destruction_breakdown,
        'selfEffectDefeats': self_effect_defeats,
        'blunderRateCoverage': rate_coverage,
        'blundersPer100': {role: {name: 100 * value / max(1, len(matches)) for name, value in counts.items()}
                           for role, counts in rate_counts.items()},
        'firstTurnDrawHandtrapActivationsOutsideChain': first_turn_draw_handtraps,
        **({'gxThemeMetrics': gx_metrics} if gx_metrics['candidate'] else {}),
        'byStyleAndSeat': dict(style_report),
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
    if (before['games'] != after['games'] or before['opening'].keys() != after['opening'].keys() or
            before.get('baselineManifestSha256') != after.get('baselineManifestSha256')):
        raise ValueError('Acceptance requires the same fixed suite used for the baseline.')
    opening_gains = {}
    for category, scores in before['opening'].items():
        _, _, baseline_successes, baseline_total = scores
        _, _, successes, total = after['opening'][category]
        if not baseline_total or not total:
            raise ValueError(f'No comparable openings for {category}.')
        opening_gains[category] = 100 * (successes / total - baseline_successes / baseline_total)
    checks = {
        'atLeast100MatchesAndSignificantWinGain': after['games'] >= 100 and after['pairedOneSidedP'] < .05,
        'winGainAtLeast8Points': before['winRate'] is not None and after['winRate'] is not None and
                                after['winRate'] >= .58 and after['winRate'] - before['winRate'] >= .08,
        'meanEndBoardImproved': after['decisionAndBoard']['candidate']['meanEndBoard'] >
                                after['decisionAndBoard']['baseline']['meanEndBoard'],
        'everyEraImproved': all(gain > 0 for name, gain in opening_gains.items() if not name.startswith('method-')),
        'noSummonMethodRegressed': all(gain >= 0 for name, gain in opening_gains.items() if name.startswith('method-')),
        'zeroCriticalBlunders': not any(after['blunders']['candidate'].values()),
        'noIncompleteMatchesOrNativeErrors': not any(after['outcomes'].get(key, 0) for key in ('unfinished', 'nativeErrors')),
        'averageDecisionAtMost300Ms': after['decisionAndBoard']['candidate']['meanDecisionMs'] <= 300,
    }
    return {'checks': checks, 'openingGainPercentagePoints': opening_gains, 'accepted': all(checks.values())}


def candidate_styles(summary):
    styles = {}
    for name, seats in summary['byStyleAndSeat'].items():
        totals = Counter()
        for policies in seats.values():
            totals.update({key: policies['candidate'].get(key, 0) for key in
                           ('games', 'winPoints', 'openingSuccesses', 'openingAttempts')})
        styles[name] = totals
    return styles


def first_replay_difference(before, after):
    old, new = before.get('replay'), after.get('replay')
    if not old or not new:
        return 'unavailable', None
    prompt_fields = ('turn', 'phase', 'player', 'message', 'hint')
    for step, (prior, current) in enumerate(zip(old, new)):
        old_prompt = tuple(prior[field] for field in prompt_fields)
        new_prompt = tuple(current[field] for field in prompt_fields)
        if old_prompt != new_prompt:
            return 'differentPrompt', step
        if prior['answer'] != current['answer']:
            return ('candidate' if prior['player'] == before['candidate'] else 'reference'), step
    if len(old) != len(new):
        return 'differentLength', min(len(old), len(new))
    return 'identical', None


def stage_audit(stage, previous, matches, summary, manifest):
    before = json.loads((previous / 'summary.json').read_bytes())
    if before.get('opponent', 'Baseline-v1') != 'Baseline-v1' or before.get('baselineCommit') != BASELINE_V1 or \
            summary['baselineCommit'] != BASELINE_V1 or \
            before.get('baselineManifestSha256') != summary['baselineManifestSha256']:
        raise ValueError('Adoption stages require both versions measured against the same pinned Baseline-v1.')
    prior_manifest = (previous / 'candidate-source-0.json').read_bytes()
    prior_source = json.loads(prior_manifest)
    current_source = json.loads(manifest)
    def measurement(source):
        return {path: digest for path, digest in source.items() if path.startswith('Assets/Scripts/Core/') or
                path == 'Assets/Scripts/Editor/AiMatchBenchmark.cs'}
    if measurement(prior_source) != measurement(current_source):
        raise ValueError('Adoption stages require the same protocol host and benchmark telemetry.')

    def index(records):
        return {(record['matchup'], record['seed'], record['candidate']): record for record in records}

    previous_records = [record for path in sorted(previous.glob('matches-*.jsonl')) for record in read_replay_report(path)]
    original, changed = index(previous_records), index(matches)
    if len(previous_records) != len(original) or len(original) != before['games'] or original.keys() != changed.keys():
        raise ValueError('Adoption stages require identical, complete seat-swapped fixtures.')

    differences = Counter()
    first_differences = Counter()
    changed_winners = Counter()
    changed_openings = Counter()
    replay_examples = []
    def win_points(record):
        return .5 if record['winner'] == 2 else int(record['winner'] == record['candidate'])

    for key, game in changed.items():
        prior = original[key]
        if game['era'] != prior['era'] or [side['deck'] for side in game['sides']] != [side['deck'] for side in prior['sides']]:
            raise ValueError(f'Adoption fixture changed: {key}')
        if [side['initial'] for side in game['sides']] != [side['initial'] for side in prior['sides']]:
            raise ValueError(f'Opening hands changed between adoption runs: {key}')
        differences[key[:2]] += win_points(game) - win_points(prior)
        first, step = first_replay_difference(prior, game)
        first_differences[first] += 1
        winner_changed = prior['winner'] != game['winner']
        old_side, new_side = prior['sides'][key[2]], game['sides'][key[2]]
        opening_changed = (old_side['methodReached'], old_side['aceReached']) != (new_side['methodReached'], new_side['aceReached'])
        if winner_changed:
            changed_winners[first] += 1
        if opening_changed:
            changed_openings[first] += 1
        if winner_changed or opening_changed:
            replay_examples.append({'matchup': key[0], 'seed': key[1], 'candidateSeat': key[2],
                                    'firstDifference': first, 'step': step, 'winnerChanged': winner_changed,
                                    'openingChanged': opening_changed})
    positive = sum(delta > 0 for delta in differences.values())
    negative = sum(delta < 0 for delta in differences.values())
    informative = positive + negative
    p_value = sum(math.comb(informative, k) for k in range(positive, informative + 1)) / 2 ** informative

    prior_styles, current_styles = candidate_styles(before), candidate_styles(summary)
    required_styles = {'Development', 'Control', 'Lock', 'Resource'}
    styles_preserved = prior_styles.keys() == current_styles.keys() == required_styles and all(
        prior_styles[name]['openingAttempts'] and current_styles[name]['openingAttempts'] and
        current_styles[name]['openingSuccesses'] * prior_styles[name]['openingAttempts'] >=
        prior_styles[name]['openingSuccesses'] * current_styles[name]['openingAttempts']
        for name in required_styles)
    methods = {key for key in before['opening'] if key.startswith('method-')}
    methods_preserved = len(methods) >= 6 and methods == {
        key for key in summary['opening'] if key.startswith('method-')} and all(
        before['opening'][key][3] and summary['opening'][key][3] and
        summary['opening'][key][2] * before['opening'][key][3] >= before['opening'][key][2] * summary['opening'][key][3]
        for key in methods)
    prior_blunders = before.get('blundersPer100', {}).get('candidate', {})
    current_blunders = summary['blundersPer100']['candidate']
    before_time = before['decisionAndBoard']['candidate']
    after_time = summary['decisionAndBoard']['candidate']
    checks = {
        'completedGameCount': len(matches) >= (100 if stage == 'screen' else 400),
        'cleanNativeCompletion': all(not report['outcomes'].get(key, 0) for report in (before, summary)
                                     for key in ('unfinished', 'nativeErrors')) and
                                 all(side['retries'] == 0 for report in (before, summary)
                                     for side in report['blunders'].values()),
        'significantWinGain': sum(differences.values()) > 0 and p_value < .05,
        'meanTurnEndBoardImproved': after_time['meanEndBoard'] > before_time['meanEndBoard'],
        'everyStylePreserved': bool(styles_preserved),
        'everySummonMethodPreserved': bool(methods_preserved),
        'fewerBlunders': bool(before.get('blunderRateCoverage') and summary['blunderRateCoverage'] and
                             prior_blunders.keys() == current_blunders.keys() and
                             all(current_blunders[key] <= prior_blunders[key] for key in current_blunders) and
                             (sum(current_blunders.values()) < sum(prior_blunders.values()) or
                              sum(current_blunders.values()) == sum(prior_blunders.values()) == 0)),
        'timing': all(record['sides'][record['candidate']].get('ordinaryTimingSeparated') for record in matches) and
                  after_time['meanOrdinaryDecisionMs'] <= 100 and after_time['slowestOrdinaryMs'] <= 300 and
                  0 < after_time['maxPlanMs'] <= 1000,
    }
    replay_comparison = {
        'identicalCandidateInputs': manifest == prior_manifest and
                                    before['candidatePolicySha256'] == summary['candidatePolicySha256'],
        'firstDifferences': dict(first_differences),
        'changedWinners': dict(changed_winners),
        'changedOpenings': dict(changed_openings),
        'changedFixtures': sorted(replay_examples, key=lambda item: (item['matchup'], item['seed'], item['candidateSeat'])),
    }
    return {'stage': stage, 'checks': checks, 'accepted': all(checks.values()),
            'pairedOneSidedP': p_value, 'improvedPairs': positive, 'regressedPairs': negative,
            'previousSourceManifestSha256': before['candidateSourceManifestSha256'],
            'previousPolicySha256': before['candidatePolicySha256'], 'replayComparison': replay_comparison}


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
    parser.add_argument('--random-opponent', action='store_true', help='Measure the separate fixed RandomLegal-v1 floor.')
    suite = parser.add_mutually_exclusive_group()
    suite.add_argument('--style-suite', action='store_true', help='Run added trap, lock, burn and alternate-win matchups.')
    suite.add_argument('--full-suite', action='store_true', help='Run all 22 fixed era, summon-method and playstyle matchups.')
    suite.add_argument('--gx-suite', action='store_true', help='Run the ten GX story and competitive theme matchups.')
    suite.add_argument('--fixed-regressions', action='store_true', help='Replay every fixed scenario with the current native AI.')
    suite.add_argument('--roster-levels', action='store_true', help='Check every available deck in both seats with the candidate AI through turn two.')
    parser.add_argument('--openings-only', action='store_true', help='Measure the first two duel turns without scoring match winners.')
    parser.add_argument('--candidate-policies', type=Path, help='Evaluate policies from an isolated training project.')
    parser.add_argument('--candidate-revision', help='Use a pinned Git revision of the candidate AI and its included policies.')
    parser.add_argument('--baseline-revision', default=BASELINE_V1,
                        help='Compare against a separately pinned Git revision. The default is Baseline-v1.')
    parser.add_argument('--stage', choices=('screen', 'confirm'), help='Apply the fixed 100/400-game adoption checks.')
    prior = parser.add_mutually_exclusive_group()
    prior.add_argument('--previous-run', type=Path, help='The previous AI measured on the same fixtures against Baseline-v1.')
    prior.add_argument('--paired-previous-revision', metavar='REV',
                       help='Interleave this pinned previous AI and the candidate against Baseline-v1 in each Unity worker.')
    parser.add_argument('--screen-run', type=Path, help='The passed screening run for the current source and policy hashes.')
    parser.add_argument('--regression-run', type=Path, help='The passed fixed native regressions for these source and policy hashes.')
    parser.add_argument('--random-floor-run', type=Path, help='The passed RandomLegal-v1 run for these source and policy hashes.')
    parser.add_argument('--require-gates', action='store_true', help='Fail unless all improvement gates pass.')
    args = parser.parse_args()
    if not re.fullmatch(r'[\w-]+', args.label) or not 1 <= args.jobs <= 4 or args.seeds < 1:
        parser.error('Use a simple run label and positive job/seed counts.')
    matchups = 10 if args.gx_suite else 22 if args.full_suite else 8 if args.style_suite else 14
    if args.jobs < 2 and not (args.fixture or args.replay or args.fixed_regressions):
        parser.error('Full benchmarks require two to four workers.')
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
    if args.random_opponent and (args.reference_only or args.replay or args.roster_levels or args.openings_only or
                                 args.fixed_regressions or args.stage or args.require_gates):
        parser.error('The random opponent is a separate completed-duel benchmark, not an adoption stage.')
    if args.paired_previous_revision and (args.reference_only or args.random_opponent or args.replay or
                                         args.roster_levels or args.fixed_regressions):
        parser.error('Interleaved revision comparison requires duel suites or --fixture against Baseline-v1.')
    if args.stage and (not args.full_suite or not (args.previous_run or args.paired_previous_revision) or
                       args.fixture or args.replay or args.reference_only or args.openings_only):
        parser.error('An adoption stage requires --full-suite, a --previous-run or --paired-previous-revision, '
                     'and completed duels against Baseline-v1.')
    if args.stage and 2 * matchups * args.seeds < (100 if args.stage == 'screen' else 400):
        parser.error('Too few fixtures for the requested adoption stage.')
    if args.stage == 'confirm' and (not args.screen_run or not args.regression_run or not args.random_floor_run):
        parser.error('Confirmation requires a passed --screen-run, --regression-run and --random-floor-run.')
    if args.fixed_regressions and (args.reference_only or args.replay or args.fixture or args.openings_only or args.require_gates):
        parser.error('Fixed regressions are a separate native verification mode.')
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
    frozen = pin_revision(args.baseline_revision)
    args.candidate_snapshot = pin_revision(args.candidate_revision) if args.candidate_revision else None
    args.previous_snapshot = pin_revision(args.paired_previous_revision) if args.paired_previous_revision else None
    if args.previous_snapshot is not None:
        if json.loads((frozen / 'manifest.json').read_bytes())['gitHead'] != BASELINE_V1:
            parser.error('Interleaved revision comparison requires the immutable Baseline-v1 opponent.')
        previous_ai = (args.previous_snapshot / 'Assets/Scripts/AI/TacticalAI.cs').read_text(encoding='utf-8-sig')
        if any(name not in previous_ai for name in ('AdvanceComboPlanning', 'SimulatedComboBranches', 'ChosenComboAlternatives')):
            parser.error('The paired previous revision must expose combo-planning and alternative-count telemetry.')
    if args.stage == 'confirm':
        screen = json.loads((args.screen_run / 'summary.json').read_bytes())
        audit = screen.get('stageAudit', {})
        candidate_dir = args.candidate_policies or (args.candidate_snapshot or PROJECT) / 'Assets/Resources'
        current_policy_hashes = policy_hashes(candidate_dir)
        previous = (dict(candidateSourceManifestSha256=hashlib.sha256(
                            source_manifest(PROJECT, args.previous_snapshot)).hexdigest(),
                         candidatePolicySha256=policy_hashes(args.previous_snapshot / 'Assets/Resources'))
                    if args.previous_snapshot is not None else
                    json.loads((args.previous_run / 'summary.json').read_bytes()))
        if (audit.get('accepted') is not True or audit.get('stage') != 'screen' or
                screen.get('baselineManifestSha256') != hashlib.sha256((frozen / 'manifest.json').read_bytes()).hexdigest() or
                screen.get('candidateSourceManifestSha256') != hashlib.sha256(source_manifest(PROJECT, args.candidate_snapshot)).hexdigest() or
                screen.get('candidatePolicySha256') != current_policy_hashes or
                audit.get('previousSourceManifestSha256') != previous.get('candidateSourceManifestSha256') or
                audit.get('previousPolicySha256') != previous.get('candidatePolicySha256')):
            parser.error('Confirmation requires the same source and policies as the passed screen, including the previous AI.')
        if not verify_regression_report(args.regression_run, json.loads(source_manifest(PROJECT, args.candidate_snapshot)),
                                        current_policy_hashes):
            parser.error('Confirmation requires every fixed scenario to pass with the same source and policies.')
        floor = json.loads((args.random_floor_run / 'summary.json').read_bytes())
        if (floor.get('opponent') != 'RandomLegal-v1' or floor.get('randomFloor', {}).get('accepted') is not True or
                any(floor.get(key) != screen.get(key) for key in
                    ('baselineManifestSha256', 'candidateSourceManifestSha256', 'candidatePolicySha256'))):
            parser.error('Confirmation requires the same source and policies to pass the random opponent floor.')
    output = args.workspace / args.label
    output.mkdir(parents=True, exist_ok=False)
    if args.gx_suite:
        args.gx_deck_inputs = {name: (PROJECT / 'Assets/StreamingAssets' / name).read_bytes() for name in GX_DECK_INPUTS}
        hashes = {name: hashlib.sha256(contents).hexdigest() for name, contents in args.gx_deck_inputs.items()}
        write(output / 'gx-decks-sha256.json', json.dumps(hashes, indent=2).encode())
    if args.replay:
        write(output / 'replay-input.jsonl', ''.join(json.dumps(match, ensure_ascii=False) + '\n' for match in selected).encode())
    version = (frozen / 'ProjectSettings/ProjectVersion.txt').read_text().splitlines()[0].split()[-1]
    editor = args.unity or Path(f'C:/Program Files/Unity/Hub/Editor/{version}/Editor/Unity.exe')
    if not editor.is_file():
        raise FileNotFoundError(f'Unity Editor: {editor}')
    if args.fixed_regressions:
        run_fixed_regressions(args, editor, frozen, output)
        return
    with ThreadPoolExecutor(max_workers=args.jobs) as pool:
        futures = [pool.submit(run_worker, i, args, editor, frozen, output) for i in range(args.jobs)]
        matches = [record for future in futures for record in future.result()]
    if args.replay:
        if len(matches) != len(selected):
            raise RuntimeError('Replay did not verify every requested match.')
        print(f'Verified {len(matches)} exact replay(s): {output}')
        return
    source_manifests = [(output / f'candidate-source-{index}.json').read_bytes() for index in range(args.jobs)]
    if any(manifest != source_manifests[0] for manifest in source_manifests[1:]):
        raise RuntimeError('Benchmark workers used different candidate source files.')
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
    if not complete_fixture_set(matches, expected):
        raise RuntimeError('Benchmark result is missing fixed fixtures or contains duplicates.')
    worker_policies = [policy_hashes(worker_path(args.workspace, frozen, index, args.candidate_snapshot,
                                                args.previous_snapshot) / 'Assets/Resources')
                       for index in range(args.jobs)]
    if any(policies != worker_policies[0] for policies in worker_policies[1:]):
        raise RuntimeError('Benchmark workers used different candidate policies.')
    summary = summary_identity(summarize(matches), frozen, source_manifests[0],
                               worker_policies[0],
                               'RandomLegal-v1' if args.random_opponent else None)
    previous_output = args.previous_run
    if args.previous_snapshot is not None:
        previous_output = output / 'previous'
        previous_manifests = [(previous_output / f'candidate-source-{index}.json').read_bytes()
                              for index in range(args.jobs)]
        if any(manifest != previous_manifests[0] for manifest in previous_manifests[1:]):
            raise RuntimeError('Benchmark workers used different previous source files.')
        previous_matches = [record for index in range(args.jobs)
                            for record in read_replay_report(previous_output / f'matches-{index}.jsonl')]
        if not complete_fixture_set(previous_matches, expected):
            raise RuntimeError('Previous benchmark result is missing fixed fixtures or contains duplicates.')
        verify_interleaved_inputs(previous_matches, matches)
        previous_worker_policies = [policy_hashes(worker_path(args.workspace, frozen, index, args.candidate_snapshot,
                                                             args.previous_snapshot) / 'Assets/Resources', 'BenchmarkPrevious')
                                    for index in range(args.jobs)]
        if any(policies != policy_hashes(args.previous_snapshot / 'Assets/Resources')
               for policies in previous_worker_policies):
            raise RuntimeError('Benchmark workers used previous policies that differ from the pinned revision.')
        previous_summary = summary_identity(summarize(previous_matches), frozen, previous_manifests[0], previous_worker_policies[0])
        previous_summary['candidateRevision'] = json.loads(
            (args.previous_snapshot / 'manifest.json').read_bytes())['gitHead']
        write(previous_output / 'summary.json', (json.dumps(previous_summary, ensure_ascii=False, indent=2) + '\n').encode())
        summary['pairedPrevious'] = {
            'revision': previous_summary['candidateRevision'], 'reportDirectory': 'previous',
            'candidateSourceManifestSha256': previous_summary['candidateSourceManifestSha256'],
            'candidatePolicySha256': previous_summary['candidatePolicySha256'],
        }
    if args.random_opponent:
        wins = summary['outcomes'].get('win', 0)
        summary['randomFloor'] = {'winRate': wins / len(matches), 'accepted': len(matches) >= 100 and
                                   wins / len(matches) >= .95 and
                                   not any(summary['outcomes'].get(key, 0) for key in ('unfinished', 'nativeErrors')) and
                                   all(side['retries'] == 0 for side in summary['blunders'].values())}
    if not args.reference_only and not args.random_opponent and summary['baselineCommit'] == BASELINE_V1 and summary['winRate'] is not None:
        styles = candidate_styles(summary)
        summary['baselineV1Targets'] = {'intermediate60Percent': summary['winRate'] >= .60,
                                        'overall70Percent': summary['winRate'] >= .70,
                                        'eachStyle55Percent': all(name in styles and styles[name]['games'] and
                                            styles[name]['winPoints'] / styles[name]['games'] >= .55 for name in
                                            ('Development', 'Control', 'Lock', 'Resource'))}
    if args.stage:
        audit = stage_audit(args.stage, previous_output, matches, summary, source_manifests[0])
        if args.stage == 'confirm':
            screen = json.loads((args.screen_run / 'summary.json').read_bytes())
            prior_audit = screen.get('stageAudit', {})
            audit['checks']['samePassedScreen'] = prior_audit.get('accepted') is True and prior_audit.get('stage') == 'screen' and all(
                screen.get(key) == summary.get(key) for key in ('baselineManifestSha256', 'candidateSourceManifestSha256', 'candidatePolicySha256')) and all(
                prior_audit.get(key) == audit.get(key) for key in ('previousSourceManifestSha256', 'previousPolicySha256'))
            audit['accepted'] = all(audit['checks'].values())
        summary['stageAudit'] = audit
    report = args.workspace / 'before' / 'summary.json'
    if not args.fixture and not args.reference_only and not args.random_opponent and not args.style_suite and not args.openings_only and report.exists():
        before = json.loads(report.read_bytes())
        if before['games'] == len(matches) and before.get('baselineManifestSha256') == summary['baselineManifestSha256']:
            summary['acceptance'] = acceptance(before, summary)
    write(output / 'summary.json', (json.dumps(summary, ensure_ascii=False, indent=2) + '\n').encode())
    print(json.dumps({key: summary[key] for key in ('games', 'outcomes', 'gainPercentagePoints', 'pairedOneSidedP',
                                                   'opening', 'blundersPer100', 'decisionAndBoard', 'stageAudit', 'randomFloor')
                      if key in summary}, indent=2))
    print(f'Report: {output / "summary.json"}')
    if args.require_gates and not summary.get('stageAudit' if args.stage else 'acceptance', {}).get('accepted', False):
        raise SystemExit(2)
    if args.stage and not summary['stageAudit']['accepted']:
        raise SystemExit(2)


if __name__ == '__main__':
    main()
