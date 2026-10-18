"""Single-writer fixtures for existing regression suites, not a production client.

These suites exercise unrelated imports/quantities/auth behavior with a freshly read version.
concurrency_integration.py deliberately DOES NOT use this helper: it submits displayed old tokens.
"""
import json
import re
import urllib.error
import urllib.request


def with_current_version(opener, base, method, path, body, headers):
    headers = dict(headers)
    if method not in ('POST', 'PUT', 'DELETE'):
        return body, headers
    recipe = re.fullmatch(r'(/api/cookbooks/[^/]+/recipes/[^/]+)(/image)?', path)
    book = re.fullmatch(r'(/api/cookbooks/[^/]+)(/image|/members(?:/[^/]+)?|/leave|/invitations/[^/]+)?', path)
    invitation = re.fullmatch(r'/api/cookbook-invitations/([^/]+)', path)
    if not (recipe or book or invitation):
        return body, headers
    if method == 'POST' and not (invitation or (book and book[2] in ('/members', '/leave'))):
        return body, headers
    url = '/api/cookbook-invitations' if invitation else recipe[1] if recipe else book[1]
    # An unavailable target still receives a syntactically valid opaque token. The mutation
    # must independently reject access/existence; these fixtures never infer permission from a GET.
    version = '3fc91b09-7c1c-4679-9b19-70df4f35384c'
    try:
        with opener.open(urllib.request.Request(base + url), timeout=40) as response:
            current = json.loads(response.read())
        if invitation:
            version = next((x['cookbookVersion'] for x in current if x['id'] == invitation[1]), version)
        else:
            version = current.get('version', version)
    except urllib.error.HTTPError:
        pass
    if method == 'DELETE' or (method == 'PUT' and path.endswith('/image')):
        headers.setdefault('If-Match', '"' + version + '"')
    elif isinstance(body, dict) or (book and book[2] == '/leave'):
        body = body or {}
        body = {**body, 'expectedVersion': body.get('expectedVersion', version)}
    return body, headers
