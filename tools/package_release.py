"""Package the allowlisted Windows release with verified SHA-256 checksums."""
import hashlib
import os
import shutil
import sys
import zipfile
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
RELEASE = ROOT / 'release'
ARTIFACTS = ROOT / 'artifacts'
NAME = 'IdleLegions-AutoAbyss-v1.2.1-win-x64.zip'
PREFIX = 'IdleLegions自动下井-v1.2.1/'
DOCS = ['使用说明.md', '模型说明.md', '历史实测报告-v0.4.3.md',
        '发布验证-1.0.md', '发布验证-1.1.md', '发布验证-1.2.md', '第三方组件.txt', '默认参数.json',
        'dotnet-LICENSE.txt', 'dotnet-ThirdPartyNotices.txt']

def digest(path):
    with path.open('rb') as stream:
        return hashlib.file_digest(stream, 'sha256').hexdigest() if hasattr(hashlib, 'file_digest') else hashlib.sha256(stream.read()).hexdigest()

def atomic_text(path, text):
    temporary = path.with_suffix(path.suffix + '.tmp')
    temporary.write_text(text, encoding='utf-8', newline='\n')
    os.replace(temporary, path)

def main():
    if hasattr(sys.stdout, 'reconfigure'):
        sys.stdout.reconfigure(encoding='utf-8')
    exe = RELEASE / 'IdleLegions自动下井.exe'
    if not exe.is_file():
        raise SystemExit('请先运行 build.ps1 构建 Windows EXE。')
    for name in DOCS:
        shutil.copy2(ROOT / 'docs' / name, RELEASE / name)
    shutil.copy2(ROOT / 'LICENSE', RELEASE / 'LICENSE.txt')
    files = ['IdleLegions自动下井.exe', *DOCS, 'LICENSE.txt']
    hashes = {name: digest(RELEASE / name) for name in files}
    atomic_text(RELEASE / 'SHA256SUMS.txt', ''.join(f'{value}  {name}\n' for name, value in hashes.items()))
    files.append('SHA256SUMS.txt')
    ARTIFACTS.mkdir(exist_ok=True)
    archive = ARTIFACTS / NAME
    temporary = archive.with_suffix('.zip.tmp')
    with zipfile.ZipFile(temporary, 'w', zipfile.ZIP_DEFLATED, compresslevel=7) as package:
        for name in files:
            package.write(RELEASE / name, PREFIX + name)
    with zipfile.ZipFile(temporary) as package:
        assert package.testzip() is None
        assert len(package.namelist()) == len(files)
        for name, value in hashes.items():
            assert hashlib.sha256(package.read(PREFIX + name)).hexdigest() == value
    os.replace(temporary, archive)
    atomic_text(ARTIFACTS / (NAME + '.sha256'), f'{digest(archive)}  {NAME}\n')
    shutil.copy2(RELEASE / 'SHA256SUMS.txt', ARTIFACTS / 'SHA256SUMS.txt')
    print(f'已校验 {len(files)} 个文件：{archive}')
    print(f'EXE SHA-256: {hashes[files[0]]}')
    print(f'ZIP SHA-256: {digest(archive)}')

if __name__ == '__main__':
    main()
