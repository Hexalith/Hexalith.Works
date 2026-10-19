"""Diagnostic: replay two keep-alive chunked POSTs through the Works sidecar to observe its raw responses."""
import http.client, json, os, sys, glob
pid = None
for p in glob.glob('/proc/[0-9]*/cmdline'):
    try:
        c = open(p, 'rb').read().split(b'\0')
    except OSError: continue
    if any(b'Hexalith.Works.dll' in x or x.endswith(b'/Hexalith.Works') for x in c):
        pid = p.split('/')[2]; break
env = dict(x.split('=', 1) for x in open(f'/proc/{pid}/environ').read().split('\0') if '=' in x)
endpoint = env['DAPR_HTTP_ENDPOINT']; token = env.get('DAPR_API_TOKEN')
print('works pid', pid, 'dapr', endpoint, 'token?', bool(token))
host, port = endpoint.replace('http://', '').split(':')
conn = http.client.HTTPConnection(host, int(port), timeout=10)
body = json.dumps({"tenant": "probe", "domain": "work", "aggregateId": "probe-1", "fromSequence": 0, "pageSize": 10}).encode()
for i in range(4):
    headers = {"dapr-app-id": "eventstore", "Content-Type": "application/json", "Transfer-Encoding": "chunked"}
    if token: headers["dapr-api-token"] = token
    def chunks():
        yield body
    conn.request("POST", "/api/v1/streams/read/workload", body=chunks(), headers=headers, encode_chunked=True)
    r = conn.getresponse(); data = r.read()
    print(i, r.status, data[:400])
