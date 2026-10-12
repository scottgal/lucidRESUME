#!/usr/bin/env python3
"""Validate a local compiler's publish/compile/download/evidence round trip.

Use an isolated sample server with unauthenticated local writes enabled. Never
point this script at a public or production service: it publishes synthetic data.
"""
import argparse
import hashlib
import http.cookiejar
import json
from pathlib import Path
import re
import urllib.error
import urllib.parse
import urllib.request

REPO = Path(__file__).resolve().parents[1]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--base-url', default='http://127.0.0.1:19825/resume')
    parser.add_argument('--output', type=Path, required=True)
    args = parser.parse_args()
    parsed = urllib.parse.urlparse(args.base_url)
    if parsed.hostname not in ['127.0.0.1', 'localhost', '::1']:
        parser.error('Use a loopback server with isolated storage.')
    args.output.mkdir(parents=True, exist_ok=True)
    base = args.base_url.rstrip('/')
    origin = f'{parsed.scheme}://{parsed.netloc}'
    opener = urllib.request.build_opener(urllib.request.HTTPCookieProcessor(http.cookiejar.CookieJar()))
    report = {'base_url': base, 'checks': []}

    def fetch(url, body=None, content_type=None, token=None):
        headers = {}
        if content_type:
            headers['Content-Type'] = content_type
        if token:
            headers['X-CSRF-TOKEN'] = token
        with opener.open(urllib.request.Request(url, data=body, headers=headers), timeout=90) as response:
            assert response.status == 200
            return response.read()

    def check(name, condition):
        assert condition, name
        report['checks'].append(name)
        print('PASS', name)

    page = fetch(base + '/').decode()
    match = re.search(r"const token='([^']+)'", page)
    check('compiler-page-and-antiforgery', match is not None)
    token = match[1]
    try:
        fetch(base + '/api/compile', b'{"jobDescription":"test"}', 'application/json')
        raise AssertionError('Request without CSRF token succeeded')
    except urllib.error.HTTPError as error:
        check('rejects-missing-antiforgery', error.code == 400)
    source = (REPO / 'samples/lucidRESUME.Web.Sample/example-complete-resume.md').read_bytes()
    published = json.loads(fetch(base + '/api/career-record', source, 'text/markdown', token))
    check('publish-complete-record', bool(published['revision']))
    check('immutable-career-download', b'career_record' in fetch(base + '/api/jobml/' + published['revision']))
    request = {'jobDescription': 'Head of Engineering. Lead a TypeScript team on AWS with release and security governance.',
               'polish': False, 'publish': True, 'includeCitations': True, 'applicationReference': '2.5 synthetic validation'}
    compiled = json.loads(fetch(base + '/api/compile', json.dumps(request).encode(), 'application/json', token))
    (args.output / 'compile.json').write_text(json.dumps(compiled, indent=2))
    check('compile-accepted-prose', bool(compiled['manifest']['sections']) and not compiled['usedCompositionProvider'])
    publication = compiled['publication']
    check('publication-evidence-page', b'<!' in fetch(origin + publication['url']))
    check('full-jobml', b'jobml:' in fetch(origin + publication['jobml']))
    check('compact-citations', bool(fetch(origin + publication['cjobml'])))
    check('complete-transcript', b'Alex Example' in fetch(origin + publication['transcript']))
    for kind, signature in [('markdown', b'#'), ('docx', b'PK'), ('pdf', b'%PDF')]:
        data = fetch(origin + compiled['downloads'][kind])
        check('download-' + kind, data.startswith(signature))
        (args.output / ('resume.' + {'markdown': 'md', 'docx': 'docx', 'pdf': 'pdf'}[kind])).write_bytes(data)
    chunks = json.loads(fetch(origin + publication['transcriptChunks']))
    reassembled = b''
    for chunk in chunks['chunks']:
        data = fetch(origin + chunk['url'])
        check('chunk-' + str(chunk['index']) + '-hash', hashlib.sha256(data).hexdigest() == chunk['sha256'])
        reassembled += data
    check('transcript-reassembly', hashlib.sha256(reassembled).hexdigest() == chunks['sourceSha256'])
    report['passed'] = True
    (args.output / 'report.json').write_text(json.dumps(report, indent=2))


if __name__ == '__main__':
    main()
