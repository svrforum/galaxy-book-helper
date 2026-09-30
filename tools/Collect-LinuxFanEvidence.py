#!/usr/bin/env python3
"""Read-only Linux ACPI/fan evidence collector for Galaxy Book6 Pro.
No EC port access, /dev/mem, module loading, profile writes, or stress load.
Run on the physical machine in a Linux live session; WSL is not sufficient.
"""
import argparse
import hashlib
import json
import os
from pathlib import Path
import platform
import time


def read(path):
    try:
        return path.read_text().strip()
    except (OSError, UnicodeError):
        return None


def collect(destination):
    if platform.system() != 'Linux':
        raise SystemExit('Run in a physical Linux live session, not Windows.')
    if 'microsoft' in platform.release().lower():
        raise SystemExit('WSL cannot inspect the host firmware. Use a physical live session.')
    destination.mkdir(parents=True, exist_ok=False)
    report = {'kernel': platform.release(), 'settings_writes': False, 'identity': {}, 'tables': [], 'fans': [], 'samples': [], 'errors': []}
    dmi = Path('/sys/class/dmi/id')
    for key in ('sys_vendor', 'product_name', 'board_name', 'bios_version'):
        report['identity'][key] = read(dmi / key)
    tables_dir = destination / 'acpi'
    tables_dir.mkdir()
    for root in (Path('/sys/firmware/acpi/tables'), Path('/sys/firmware/acpi/tables/dynamic')):
        if not root.exists():
            report['errors'].append('Missing table directory: ' + str(root))
            continue
        for source in sorted(root.iterdir()):
            # Only AML tables, excluding identity/licensing and unrelated firmware data.
            if not source.is_file() or not source.name.startswith(('DSDT', 'SSDT')):
                continue
            try:
                data = source.read_bytes()
                if data[:4] not in (b'DSDT', b'SSDT') or len(data) < 36:
                    raise ValueError('Invalid AML table header')
                declared = int.from_bytes(data[4:8], 'little')
                if declared != len(data) or sum(data) % 256:
                    raise ValueError('Invalid AML table length/checksum')
                name = ('dynamic-' if root.name == 'dynamic' else '') + source.name + '.dat'
                (tables_dir / name).write_bytes(data)
                report['tables'].append({'file': name, 'bytes': len(data), 'sha256': hashlib.sha256(data).hexdigest()})
            except (OSError, ValueError) as error:
                report['errors'].append(str(source) + ': ' + str(error))
    sensors = []
    for root in sorted(Path('/sys/class/hwmon').glob('hwmon*')):
        for path in sorted(root.glob('fan*_input')):
            sensors.append(path)
            report['fans'].append({'path': str(path), 'name': read(root / 'name'), 'label': read(root / path.name.replace('_input', '_label')), 'device': str((root / 'device').resolve())})
    acpi_paths = list(Path('/sys/bus/acpi/devices').glob('PNP0C0B:*/fan_speed_rpm'))
    acpi_paths += list(Path('/sys/bus/platform/devices').glob('PNP0C0B:*/fan_speed_rpm'))
    seen = {str(path.resolve()) for path in sensors}
    for path in sorted(acpi_paths):
        if str(path.resolve()) in seen:
            continue
        seen.add(str(path.resolve()))
        sensors.append(path)
        report['fans'].append({'path': str(path), 'name': path.parent.name, 'device': str(path.parent.resolve())})
    for index in range(10):
        report['samples'].append({'second': index, 'readings': {str(path): read(path) for path in sensors}})
        if sensors and index != 9:
            time.sleep(1)
        if not sensors:
            break
    report['profiles'] = {name: read(Path('/sys/firmware/acpi') / name) for name in ('platform_profile', 'platform_profile_choices')}
    report['rpm_observed'] = any(value is not None and value.isdigit() and 0 <= int(value) < 100000 for sample in report['samples'] for value in sample['readings'].values())
    (destination / 'report.json').write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding='utf-8')
    print(json.dumps({'output': str(destination.resolve()), 'tables': len(report['tables']), 'fan_paths': len(sensors), 'rpm_observed': report['rpm_observed'], 'errors': report['errors']}, ensure_ascii=False, indent=2))


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--output', type=Path, default=Path('galaxy-fan-evidence-' + time.strftime('%Y%m%d-%H%M%S')))
    args = parser.parse_args()
    collect(args.output)
