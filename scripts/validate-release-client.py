#!/usr/bin/env python3
"""Native release workflow checks using an isolated store and synthetic DOCX inputs.

Build Release first, then run with --app pointing at lucidRESUME.dll or a packaged
executable. Output contains structured results, client logs and real screenshots.
No cloud accounts or private resume fixtures are required.
"""
import argparse
import json
import os
from pathlib import Path
import queue
import sqlite3
import subprocess
import threading
import time
import zipfile
from xml.sax.saxutils import escape

REPO = Path(__file__).resolve().parents[1]


def docx(path, additional=False):
    paragraphs = [
        ('Title', 'Alex Example'), ('', 'alex@example.com | London, UK'),
        ('Heading1', 'Professional Summary'),
        ('', 'Software engineer delivering reliable C# and ASP.NET Core services.'),
        ('Heading1', 'Experience'),
        ('Heading2', 'Senior Software Engineer | Example Systems'),
        ('', 'January 2020 - Present'),
        ('', 'Built C# and ASP.NET Core APIs with PostgreSQL for customer workflows.'),
        ('', 'Implemented automated regression testing and production observability.'),
        ('Heading1', 'Skills'), ('', 'C#, ASP.NET Core, PostgreSQL, Git, Testing'),
        ('Heading1', 'Education'), ('', 'BSc Computer Science | Example University | 2019')]
    if additional:
        paragraphs[7:7] = [('', 'Maintained OpenTelemetry traces for incident investigation.')]
    body = ''.join('<w:p><w:pPr><w:pStyle w:val="' + style + '"/></w:pPr><w:r><w:t>' + escape(text) + '</w:t></w:r></w:p>' for style, text in paragraphs)
    with zipfile.ZipFile(path, 'w') as z:
        z.writestr('[Content_Types].xml', '<Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"><Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/><Default Extension="xml" ContentType="application/xml"/><Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/></Types>')
        z.writestr('_rels/.rels', '<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml"/></Relationships>')
        z.writestr('word/document.xml', '<w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"><w:body>' + body + '</w:body></w:document>')


class Client:
    def __init__(self, app, output):
        self.output = output
        self.pending = {}
        self.counter = 0
        env = os.environ.copy()
        env.update(LUCIDRESUME_DATA_DIR=str(output / 'data'),
                   LUCIDRESUME_Startup__AutoDownloadModels='false',
                   LUCIDRESUME_Tailoring__Provider='ollama',
                   LUCIDRESUME_Tailoring__ExtractionProvider='none',
                   LUCIDRESUME_Ollama__BaseUrl='http://127.0.0.1:1',
                   LUCIDRESUME_Interview__Enabled='false',
                   LUCIDRESUME_Docling__Enabled='false')
        command = (['dotnet', str(app)] if app.suffix == '.dll' else [str(app)])
        self.stderr = (output / 'client-stderr.log').open('w', encoding='utf-8')
        self.log = (output / 'client-stdout.log').open('w', encoding='utf-8')
        self.proc = subprocess.Popen(command + ['--ux-mcp', '--output', str(output / 'screenshots')],
                                     cwd=REPO, env=env, stdin=subprocess.PIPE,
                                     stdout=subprocess.PIPE, stderr=self.stderr, text=True,
                                     encoding='utf-8', bufsize=1)
        threading.Thread(target=self.read, daemon=True).start()

    def read(self):
        for line in self.proc.stdout:
            self.log.write(line)
            self.log.flush()
            try:
                message = json.loads(line.lstrip('\ufeff'))
                if message.get('id') in self.pending:
                    self.pending[message['id']].put(message)
            except ValueError:
                pass

    def call(self, tool, **arguments):
        self.counter += 1
        number = self.counter
        response = self.pending[number] = queue.Queue()
        self.proc.stdin.write(json.dumps({'jsonrpc': '2.0', 'id': number, 'method': 'tools/call',
                                         'params': {'name': tool, 'arguments': arguments}}) + '\n')
        self.proc.stdin.flush()
        try:
            message = response.get(timeout=180)
        finally:
            del self.pending[number]
        if 'error' in message:
            raise RuntimeError(message['error'])
        result = '\n'.join(x.get('text', '') for x in message.get('result', {}).get('content', []))
        if message.get('result', {}).get('isError') or result.startswith('Error:'):
            raise RuntimeError(result)
        return result

    def get(self, path):
        result = self.call('ui_get', path='CurrentPage.' + path)
        assert 'Property not found' not in result, result
        return result.split(' = ', 1)[-1]

    def wait(self, path, expected, timeout=90):
        deadline = time.monotonic() + timeout
        while time.monotonic() < deadline:
            result = self.get(path)
            if result == str(expected):
                return
            time.sleep(.2)
        raise AssertionError(f'{path}: expected {expected}, got {result}')

    def click(self, name):
        return self.call('ui_click', name=name)

    def nav(self, page):
        return self.call('ui_navigate', page=page)

    def set(self, name, value):
        return self.call('ui_set', path='CurrentPage.' + name, value=str(value))

    def shot(self, name):
        return self.call('ui_screenshot', name=name)

    def script(self, name, actions):
        # JSON syntax is valid YAML; the YAML loader accepts named action enums.
        path = self.output / (name + '.yaml')
        path.write_text(json.dumps({'name': name, 'default_delay': 200, 'actions': actions}))
        result = self.call('ui_run_script', path=str(path))
        assert 'PASS' in result, result
        return result

    def close(self):
        self.proc.terminate()
        try:
            self.proc.wait(timeout=10)
        except subprocess.TimeoutExpired:
            self.proc.kill()
            self.proc.wait()
        self.stderr.close()
        self.log.close()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--app', type=Path, default=REPO / 'src/lucidRESUME/bin/Release/net10.0/lucidRESUME.dll')
    parser.add_argument('--output', type=Path, required=True)
    args = parser.parse_args()
    output = args.output.resolve()
    output.mkdir(parents=True, exist_ok=True)
    if (output / 'data').exists():
        parser.error('Output already contains a data directory; use a fresh output path.')
    app = args.app.resolve()
    assert app.is_file(), app
    report = {'app': str(app), 'cases': [], 'started_at': time.strftime('%Y-%m-%dT%H:%M:%SZ', time.gmtime())}
    client = Client(app, output)

    def case(name, action):
        started = time.monotonic()
        item = {'name': name}
        try:
            action()
            item['status'] = 'passed'
            print('PASS', name, flush=True)
        except Exception as error:
            item.update(status='failed', error=str(error))
            print('FAIL', name, error, flush=True)
            try:
                (output / (name + '-vm.txt')).write_text(client.call('ui_vm'), encoding='utf-8')
                client.shot(name + '-failure')
            except Exception:
                pass
        item['seconds'] = round(time.monotonic() - started, 2)
        report['cases'].append(item)
        (output / 'report.json').write_text(json.dumps(report, indent=2))
        return item['status'] == 'passed'

    def navigate():
        for page in ['Resume', 'JobML', 'MyData', 'Career', 'Jobs', 'Search', 'Apply', 'Generated', 'Pipeline', 'Profile', 'Help']:
            client.nav(page)
            if page in ['Profile', 'Help']:
                client.call('ui_scroll', direction='bottom')
                client.call('ui_scroll', direction='top')
            client.shot('page-' + page.lower())

    def imported():
        for i in range(2):
            path = output / f'career-{i}.docx'
            docx(path, i == 1)
            client.script('import-' + str(i), [
                {'type': 'ImportFile', 'target': 'Fast', 'value': str(path)},
                {'type': 'Expect', 'target': 'name=ReviewImportTitle', 'matcher': 'IsVisible', 'timeout': 30000},
                {'type': 'Click', 'target': 'name=ApplyAllImportButton'},
                {'type': 'Expect', 'target': 'name=ResumeContent', 'matcher': 'IsVisible', 'timeout': 30000}])
        with sqlite3.connect(output / 'data/data.db') as db:
            career = json.loads(db.execute("select value from app_meta where key='complete_career'").fetchone()[0])
            assert len(career['Transcript']['Experience']) == 1, career['Transcript']['Experience']
            assert db.execute('select count(*) from resumes').fetchone()[0] == 2
        client.shot('reviewed-imports')

    def interview():
        client.nav('Apply')
        client.click('type=Button text="Build complete transcript"')
        client.wait('HasInterview', 'True')
        client.set('InterviewContext', 'During operation of customer-facing APIs')
        client.set('InterviewAction', 'Implemented OpenTelemetry traces and recovery runbooks')
        client.set('InterviewOutcome', 'Made incident diagnosis and recovery repeatable')
        client.set('InterviewSkills', 'OpenTelemetry')
        client.click('type=Button text="Assemble a prose draft"')
        assert 'OpenTelemetry' in client.get('InterviewProse')
        client.click('type=Button text="Save reviewed prose to role"')
        client.wait('IsTailoring', 'False')
        assert client.get('ErrorMessage') == 'null', client.get('ErrorMessage')
        with sqlite3.connect(output / 'data/data.db') as db:
            data = json.loads(db.execute("select value from app_meta where key='complete_career'").fetchone()[0])
            assert any(r['InterviewEntries'] for r in data['Transcript']['Experience'])
        client.shot('reviewed-role-interview')

    def editor():
        client.nav('JobML')
        client.wait('IsValid', 'True')
        client.wait('BrokenLinkCount', '0')
        assert 'career_record' in client.get('JobMlText')
        client.click('type=Button text="Publish"')
        deadline = time.monotonic() + 30
        while 'Published' not in client.get('StatusMessage') and time.monotonic() < deadline:
            time.sleep(.2)
        assert 'Published' in client.get('StatusMessage'), client.get('StatusMessage')
        client.shot('complete-career-published')

    def generate():
        client.nav('Apply')
        client.set('JobTitle', 'Senior Software Engineer')
        client.set('Company', 'Example Employer')
        client.set('JobDescriptionText', 'Senior Software Engineer\nExample Employer\nBuild production C# and ASP.NET Core APIs with PostgreSQL. Implement automated testing and observability. London, remote.')
        client.click('ProjectResumeButton')
        client.wait('IsTailoring', 'False')
        if client.get('HasInterview') == 'True':
            client.click('type=Button text="Project with current evidence"')
            client.wait('IsTailoring', 'False')
        assert client.get('HasResult') == 'True', client.get('StatusMessage')
        assert client.get('ErrorMessage') == 'null', client.get('ErrorMessage')
        assert 'Saved to Generated archive' in client.get('StatusMessage'), client.get('StatusMessage')
        client.shot('generated-result')

    def archive():
        client.nav('Generated')
        client.wait('IsLoading', 'False')
        client.click('PreviewArchivedPdf')
        client.wait('IsLoading', 'False')
        assert int(client.get('PageCount')) > 0
        assert client.get('ErrorMessage') == 'null', client.get('ErrorMessage')
        client.shot('archived-pdf')
        client.click('PreviewArchivedWord')
        client.wait('IsLoading', 'False')
        assert 'Word' in client.get('Status'), client.get('Status')
        assert client.get('ErrorMessage') == 'null', client.get('ErrorMessage')
        client.shot('archived-word')
        client.click('type=TabItem text="Text and citations"')
        client.click('Citation1')
        assert 'Saved source evidence' in client.get('EvidenceMarkdown'), client.get('EvidenceMarkdown')
        client.shot('archived-evidence')
        with sqlite3.connect(output / 'data/data.db') as db:
            assert db.execute('select count(*) from generated_resumes').fetchone()[0] == 1
            assert db.execute('select count(*) from resumes').fetchone()[0] == 2

    def jobs_pipeline():
        client.nav('Search')
        client.set('PastedText', 'Senior Software Engineer\nExample Employer\nBuild C# and ASP.NET Core APIs with PostgreSQL. Automated testing and observability. London, remote.')
        client.click('type=Button text="Add Job"')
        client.wait('IsLoading', 'False')
        assert 'Added:' in client.get('StatusMessage'), client.get('StatusMessage')
        client.set('NewWatchQuery', 'C# engineering')
        client.click('type=Button text="Watch"')
        client.wait('Watches.Count', '1')
        client.click('type=Button text="Delete"')
        client.wait('Watches.Count', '0')
        client.nav('Jobs')
        client.wait('IsLoading', 'False')
        client.wait('Jobs.Count', '1')
        client.click('type=Button:has-text(Senior Software Engineer)')
        client.wait('SelectedJobTitle', 'Senior Software Engineer')
        client.click('type=Button text="Track Application"')
        client.nav('Pipeline')
        client.wait('TotalApplications', '1')
        client.click('type=Button:has-text(Senior Software Engineer)')
        client.click('type=Button text="Applied"')
        client.wait('AppliedCount', '1')
        client.set('SelectedNoteText', 'Release validation note')
        client.click('type=Button text="Add"')
        client.set('RecruiterName', 'Example Recruiter')
        client.set('RecruiterEmail', 'recruiter@example.com')
        client.click('type=Button text="Save Contact"')
        client.shot('tracked-application')
        with sqlite3.connect(output / 'data/data.db') as db:
            data = json.loads(db.execute('select data from applications').fetchone()[0])
            assert data['Contact']['RecruiterName'] == 'Example Recruiter', data
            assert any('Release validation note' in json.dumps(e) for e in data['Timeline']), data
        client.nav('Jobs')
        client.set('IsHiringMode', 'True')
        client.shot('hiring-mode')
        client.set('IsHiringMode', 'False')

    def hiring():
        client.nav('Jobs')
        client.set('IsHiringMode', 'True')
        client.click('type=Button text="Save Role"')
        assert client.get('HiringValidationError') == 'Title is required.'
        client.set('HiringTitle', 'Engineering Lead')
        client.set('HiringCompany', 'Example Employer')
        client.set('HiringIsRemote', 'True')
        client.set('HiringSalaryMin', '80000')
        client.set('HiringSalaryMax', '100000')
        client.set('HiringDescription', 'Lead an engineering team building reliable APIs.')
        client.click('type=Button text="Save Role"')
        client.wait('HiringRoles.Count', '1')
        assert client.get('HiringValidationError') == 'null'
        client.shot('saved-hiring-role')
        client.set('IsHiringMode', 'False')

    def pdf_import():
        pdf = next((output / 'data/generated-resumes').glob('**/*.pdf'))
        client.nav('Resume')
        client.script('pdf-round-trip', [
            {'type': 'ImportFile', 'target': 'Fast', 'value': str(pdf)},
            {'type': 'Expect', 'target': 'name=ReviewImportTitle', 'matcher': 'IsVisible', 'timeout': 30000},
            {'type': 'Click', 'target': 'name=ApplyAllImportButton'},
            {'type': 'Expect', 'target': 'name=ResumeContent', 'matcher': 'IsVisible', 'timeout': 30000}])
        with sqlite3.connect(output / 'data/data.db') as db:
            assert db.execute('select count(*) from resumes').fetchone()[0] == 3
        client.shot('reviewed-pdf-import')

    def profile():
        client.nav('Profile')
        client.set('CareerGoals', 'Lead reliable engineering teams')
        client.set('OpenToRemote', 'True')
        client.wait('IsSaved', 'True')
        with sqlite3.connect(output / 'data/data.db') as db:
            data = json.loads(db.execute('select data from profile').fetchone()[0])
            assert 'Lead reliable engineering teams' in json.dumps(data), data
        client.shot('saved-profile')

    def restart():
        nonlocal client
        client.close()
        client = Client(app, output)
        client.nav('Resume')
        client.wait('IsLoading', 'False')
        client.nav('Generated')
        client.wait('IsLoading', 'False')
        client.wait('Entries.Count', '1')
        client.click('PreviewArchivedPdf')
        client.wait('IsLoading', 'False')
        assert int(client.get('PageCount')) > 0
        client.nav('Pipeline')
        client.wait('TotalApplications', '1')
        client.nav('Profile')
        assert client.get('CareerGoals') == 'Lead reliable engineering teams'
        client.shot('restart-preserved-profile')

    def run_linked_editor():
        result = client.call('ui_run_script', path=str(REPO / 'ux-scripts/jobml-linked-editor.yaml'))
        assert 'Result: PASS' in result, result

    try:
        case('empty-navigation', navigate)
        if case('docx-import-review-dedup', imported):
            case('complete-role-interview', interview)
            case('full-jobml-publish', editor)
            if case('generate-with-unavailable-writer', generate):
                case('archive-word-pdf-evidence', archive)
        case('jobs-watch-pipeline', jobs_pipeline)
        case('hiring-validation-save', hiring)
        case('profile-save', profile)
        case('populated-navigation', navigate)
        case('restart-persistence', restart)
        case('jobml-draft-selection-drift', run_linked_editor)
        case('pdf-import-round-trip', pdf_import)
    finally:
        client.close()
    report['passed'] = all(item['status'] == 'passed' for item in report['cases'])
    (output / 'report.json').write_text(json.dumps(report, indent=2))
    raise SystemExit(0 if report['passed'] else 1)


if __name__ == '__main__':
    main()
