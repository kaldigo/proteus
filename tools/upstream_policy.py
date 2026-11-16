"""Only untouched upstream documentation is eligible for a routine merge."""
import argparse
import json
from pathlib import Path
import subprocess

def git(*args):
    return subprocess.check_output(['git', *args], text=True).strip()

def paths(before, after):
    return set(filter(None, subprocess.check_output(
        ['git', 'diff', '--name-only', '--no-renames', '-z', before, after]
    ).decode('utf-8').split('\0')))

def classify(base, upstream, head):
    base, upstream, head = [git('rev-parse', f'{ref}^{{commit}}') for ref in (base, upstream, head)]
    common = git('merge-base', base, upstream)
    fork_paths = paths(common, base)
    changed = paths(base, head) | paths(common, upstream)
    reasons, accepted = [], []
    expected = subprocess.run(['git', 'merge-tree', '--write-tree', base, upstream], text=True, capture_output=True)
    trees = expected.stdout.splitlines()
    if expected.returncode or not trees or trees[0] != git('rev-parse', f'{head}^{{tree}}'):
        return {'base': base, 'upstream': upstream, 'head': head, 'routine': False,
                'changed_paths': sorted(changed), 'accepted': [],
                'reasons': ['Proposed tree differs from a clean merge of the recorded upstream and fork tips.']}
    for path in sorted(changed):
        modes = [git('ls-tree', ref, '--', path).split(' ', 1)[0] for ref in (common, upstream, base, head)]
        if any(mode and mode != '100644' for mode in modes):
            reasons.append(f'{path}: special file mode, executable, symlink or submodule.')
            continue
        if (path.startswith("docs/") and path.endswith(".md") and path not in fork_paths):
            accepted.append(path)
        else:
            reasons.append(f"{path}: requires maintainer review")
    return dict(base=base, upstream=upstream, head=head, routine=bool(changed) and not reasons,
                changed_paths=sorted(changed), accepted=accepted, reasons=reasons)

def report(policy):
    decision = 'Routine: eligible for automatic merge after all checks pass.' if policy['routine'] else 'Manual review required; automatic merge is disabled.'
    lines = [decision, '', 'Checks: build, configuration regressions, policy regressions and release-package validation.',
             'Routine automatic merges queue a validated, versioned release. Sensitive updates remain manual.', '', f"Validated base: `{policy['base']}`",
             f"Proposed head: `{policy['head']}`", '', 'Changed files:']
    lines.extend(f'- `{path}`' for path in policy['changed_paths'])
    if policy.get('accepted'):
        lines.extend(['', 'Automatic review evidence:'])
        lines.extend(f'- {entry}' for entry in policy['accepted'])
    if policy['reasons']:
        lines.extend(['', 'Reasons for manual review:'])
        lines.extend(f'- {reason}' for reason in policy['reasons'])
    if policy['routine']:
        receipt = {key: policy[key] for key in ['base', 'upstream', 'head']}
        lines.extend(['', '<!-- routine-release:' + json.dumps(receipt) + ' -->'])
    return '\n'.join(lines) + '\n'


if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('--base', required=True)
    parser.add_argument('--upstream', required=True)
    parser.add_argument('--head', default='HEAD')
    parser.add_argument('--output', required=True)
    args = parser.parse_args()
    policy = classify(args.base, args.upstream, args.head)
    Path(args.output).write_text(json.dumps(policy, indent=2) + '\n', encoding='utf-8')
    Path(args.output + '.md').write_text(report(policy), encoding='utf-8')
    print(report(policy))
