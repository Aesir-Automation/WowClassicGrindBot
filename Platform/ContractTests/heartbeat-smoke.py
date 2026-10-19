"""Synthetic end-to-end Verify CLI checks. Build MacOS/Verify first; no game/input."""
import json
import pathlib
import subprocess
import sys
import tempfile

root = pathlib.Path(__file__).resolve().parents[1]
verifier = root / 'MacOS/Verify/bin/Debug/net10.0/Verify.dll'
palette = json.loads((root / 'GameFixture/expected-values.json').read_text())
with tempfile.TemporaryDirectory(prefix='pixel-heartbeat-') as directory:
    temp = pathlib.Path(directory)
    layout = temp / 'layout.json'
    layout.write_text(json.dumps({'Frames': [dict(Index=i, X=i, Y=0) for i in range(20)]}))
    helper = temp / 'helper'
    helper.write_text(f'''#!{sys.executable}
import json, pathlib, struct, sys
folder = pathlib.Path(__file__).parent
state = folder / 'sequence'
n = int(state.read_text()) + 1 if state.exists() else 1
state.write_text(str(n))
mode = (folder / 'mode').read_text()
values = {palette!r}
values[17] = 42 if mode == 'frozen' else n
if mode == 'corrupt': values[1] += 1
payload = b''.join(bytes((v & 255, (v >> 8) & 255, v >> 16, 255)) for v in values)
sys.stdout.buffer.write(struct.pack('<4sIIIIfQ', b'PXF1', 20, 1, 80, 80, 1., n) + payload)
''')
    helper.chmod(0o700)
    for mode, count, passed, message in [
        ('changing', 5, True, None),
        ('frozen', 25, False, 'stalled'),
        ('corrupt', 5, False, 'cell 1'),
        ('changing', 2, False, 'two observed'),
    ]:
        (temp / 'mode').write_text(mode)
        (temp / 'sequence').unlink(missing_ok=True)
        result = subprocess.run(['dotnet', str(verifier), str(helper), '1', str(layout),
            str(root / 'GameFixture/expected-values.json'), str(count), '--heartbeat-index', '17'],
            capture_output=True, text=True, timeout=20)
        report = json.loads(result.stdout)
        assert (result.returncode == 0) == passed, (mode, result.stdout, result.stderr)
        if message:
            assert message in report['Error'], report
        else:
            assert report['HeartbeatAdvances'] == 4, report
        print(f'PASS: synthetic CLI {mode}, samples={count}, expected pass={passed}')
