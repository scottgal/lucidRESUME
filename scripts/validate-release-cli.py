#!/usr/bin/env python3
"""Exercise offline CLI workflows with synthetic inputs and inspect their artifacts."""
import argparse
import json
import os
from pathlib import Path
import runpy
import subprocess
import time

REPO = Path(__file__).resolve().parents[1]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--app', type=Path, default=REPO / 'src/lucidRESUME.Cli/bin/Release/net10.0/lucidRESUME.Cli.dll')
    parser.add_argument('--output', type=Path, required=True)
    args = parser.parse_args()
    output = args.output.resolve()
    output.mkdir(parents=True, exist_ok=True)
    fixtures = output / 'fixtures'
    fixtures.mkdir(exist_ok=True)
    make_docx = runpy.run_path(str(REPO / 'scripts/validate-release-client.py'))['docx']
    resume = fixtures / 'resume.docx'
    updated = fixtures / 'updated.docx'
    make_docx(resume)
    make_docx(updated, True)
    config = output / 'offline.json'
    config.write_text(json.dumps({'Tailoring': {'Provider': 'ollama', 'ExtractionProvider': 'none'},
                                  'Ollama': {'BaseUrl': 'http://127.0.0.1:1'},
                                  'Docling': {'Enabled': False}}))
    env = dict(os.environ, LUCIDRESUME_DATA_DIR=str(output / 'data'))
    prefix = ['dotnet', str(args.app.resolve())]
    job = 'Senior Software Engineer. Build reliable C# and ASP.NET Core APIs with PostgreSQL and automated testing.'
    portable = REPO / 'samples/lucidRESUME.Web.Sample/example-complete-resume.md'
    report = {'cases': []}

    def run(name, command, artifact=None, kind=None):
        start = time.monotonic()
        result = subprocess.run(prefix + list(map(str, command)) + ['--config', str(config)],
                                cwd=REPO, env=env, capture_output=True, text=True, timeout=120)
        (output / (name + '.log')).write_text(result.stdout + result.stderr)
        case = {'name': name, 'exit_code': result.returncode, 'seconds': round(time.monotonic() - start, 2)}
        try:
            assert result.returncode == 0, (result.stderr or result.stdout)[-1500:]
            if artifact:
                data = (output / artifact).read_bytes()
                assert data, 'Empty output'
                if kind == 'json':
                    json.loads(data)
                elif kind == 'docx':
                    assert data.startswith(b'PK')
                elif kind == 'pdf':
                    assert data.startswith(b'%PDF')
            case['status'] = 'passed'
            print('PASS', name, flush=True)
        except Exception as error:
            case.update(status='failed', error=str(error))
            print('FAIL', name, error, flush=True)
        report['cases'].append(case)
        (output / 'report.json').write_text(json.dumps(report, indent=2))

    run('parse', ['parse', '--file', resume, '--output', output / 'parsed.json'], 'parsed.json', 'json')
    run('export-json', ['export', '--file', resume, '--format', 'json', '--output', output / 'export.json'], 'export.json', 'json')
    run('export-markdown', ['export', '--file', resume, '--format', 'markdown', '--output', output / 'export.md'], 'export.md')
    run('analyse', ['analyse', '--resume', resume, '--job', job, '--output', output / 'analyse.json'], 'analyse.json', 'json')
    run('match', ['match', '--resume', resume, '--job', job])
    run('compound-match', ['compound-match', '--resumes', resume, updated, '--job', job])
    run('evidence', ['evidence', '--resume', resume, '--output', output / 'evidence.json'], 'evidence.json', 'json')
    run('explain', ['explain', '--resume', resume, '--job', job, '--output', output / 'explain.json'], 'explain.json', 'json')
    run('validate', ['validate', '--resume', resume, '--output', output / 'quality.json'], 'quality.json', 'json')
    run('extract-jd', ['extract-jd', '--job', job, '--output', output / 'job.json'], 'job.json', 'json')
    run('fix', ['fix', '--resume', resume, '--output', output / 'fixed.md'], 'fixed.md')
    run('anonymize', ['anonymize', '--resume', resume, '--output', output / 'anonymous.md'], 'anonymous.md')
    run('rank', ['rank', '--dir', fixtures, '--job', job, '--output', output / 'rank.json'], 'rank.json', 'json')
    run('drift', ['drift', '--resume1', resume, '--resume2', updated])
    run('tailor', ['tailor', '--resume', resume, '--job', job, '--output', output / 'tailored.md'], 'tailored.md')
    run('generate', ['generate', '--resume', resume, '--prompt', job, '--output', output / 'generated.md'], 'generated.md')
    run('career-record', ['jobml', 'career-record', '--resume-dir', fixtures, '--output', output / 'career.jobml.md'], 'career.jobml.md')
    # JobML inspection commands have no provider/config option.
    for name in ['validate', 'reconcile']:
        start = time.monotonic()
        result = subprocess.run(prefix + ['jobml', name, '--file', str(portable)], cwd=REPO,
                                env=env, capture_output=True, text=True, timeout=30)
        (output / ('jobml-' + name + '.log')).write_text(result.stdout + result.stderr)
        report['cases'].append({'name': 'jobml-' + name, 'status': 'passed' if result.returncode == 0 else 'failed',
                                'exit_code': result.returncode, 'seconds': round(time.monotonic() - start, 2)})
    for kind in ['docx', 'pdf']:
        run('render-' + kind, ['render', '--file', portable, '--format', kind, '--output', output / ('render.' + kind)], 'render.' + kind, kind)
    report['passed'] = all(case['status'] == 'passed' for case in report['cases'])
    (output / 'report.json').write_text(json.dumps(report, indent=2))
    raise SystemExit(0 if report['passed'] else 1)


if __name__ == '__main__':
    main()
