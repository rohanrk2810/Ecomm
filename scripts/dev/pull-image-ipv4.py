"""Import a pinned official linux/amd64 registry image over host IPv4.

Workaround for Docker Desktop registry EOFs. Uses curl with normal TLS validation,
verifies manifest/config/layer SHA256, and imports a standard Docker image archive.
Anonymous registry tokens remain in memory. No Docker/network settings are changed.
"""
import argparse
import gzip
import hashlib
import json
import pathlib
import re
import shutil
import subprocess
import tarfile
import tempfile

ACCEPT = ', '.join([
    'application/vnd.oci.image.index.v1+json',
    'application/vnd.docker.distribution.manifest.list.v2+json',
    'application/vnd.oci.image.manifest.v1+json',
    'application/vnd.docker.distribution.manifest.v2+json',
])


def verify(path, digest):
    if not digest.startswith('sha256:'):
        raise ValueError('Only SHA256 registry digests are supported')
    with path.open('rb') as stream:
        actual = hashlib.file_digest(stream, 'sha256').hexdigest()
    if actual != digest.removeprefix('sha256:'):
        raise ValueError('Registry content digest mismatch')


def fetch(url, path, token=None):
    # -q disables user curl config; TLS certificate verification remains enabled.
    command = ['curl.exe' if shutil.which('curl.exe') else 'curl', '-q', '-4', '-f', '-sS', '-L',
               '--retry', '3', '--retry-all-errors', '--retry-delay', '2',
               '--connect-timeout', '20', '--max-time', '300',
               '-H', f'Accept: {ACCEPT}', '-o', str(path), url]
    if token:
        command += ['-H', f'Authorization: Bearer {token}']
    result = subprocess.run(command, capture_output=True)
    if result.returncode:
        # Do not print URLs, authorization headers or provider redirects.
        raise RuntimeError(f'Registry transfer failed (curl exit {result.returncode})')


def import_image(image, cache_root):
    if '@' in image or ':' not in image.rsplit('/', 1)[-1]:
        raise ValueError('Supply an explicit image tag')
    name, tag = image.rsplit(':', 1)
    allowed = {'postgres', 'redis', 'apache/kafka', 'otel/opentelemetry-collector-contrib',
               'mcr.microsoft.com/azure-storage/azurite', 'mcr.microsoft.com/dotnet/sdk',
               'mcr.microsoft.com/dotnet/aspnet'}
    if name not in allowed or not re.fullmatch(r'[A-Za-z0-9_][A-Za-z0-9_.-]{0,127}', tag) or tag == 'latest':
        raise ValueError('Use a pinned version of a project official image')
    if name.startswith('mcr.microsoft.com/'):
        registry, repository = name.split('/', 1)
        token = None
    elif name.split('/')[0] in ('postgres', 'redis', 'apache', 'otel'):
        registry = 'registry-1.docker.io'
        repository = name if '/' in name else f'library/{name}'
        token = 'fetch'
    else:
        raise ValueError('Only the project official Microsoft/Docker Hub images are allowed')

    cache_root.mkdir(parents=True, exist_ok=True)
    with tempfile.TemporaryDirectory(prefix='registry-', dir=cache_root) as temporary:
        work = pathlib.Path(temporary)
        if token:
            auth_path = work / 'token.json'
            fetch(f'https://auth.docker.io/token?service=registry.docker.io&scope=repository:{repository}:pull', auth_path)
            token = json.loads(auth_path.read_text())['token']
            auth_path.unlink()
        base = f'https://{registry}/v2/{repository}'
        manifest_path = work / 'index.json'
        fetch(f'{base}/manifests/{tag}', manifest_path, token)
        manifest = json.loads(manifest_path.read_text())
        if 'manifests' in manifest:
            selected = next(item for item in manifest['manifests']
                            if item.get('platform', {}).get('os') == 'linux'
                            and item.get('platform', {}).get('architecture') == 'amd64')
            fetch(f"{base}/manifests/{selected['digest']}", manifest_path, token)
            verify(manifest_path, selected['digest'])
            manifest = json.loads(manifest_path.read_text())
        else:
            raise ValueError('Expected a multi-platform image index with a verified platform digest')
        print(f'Importing {image} (verified linux/amd64 manifest)', flush=True)
        image_files = work / 'image'
        image_files.mkdir()
        config_digest = manifest['config']['digest']
        config_name = config_digest.removeprefix('sha256:') + '.json'
        config = image_files / config_name
        fetch(f'{base}/blobs/{config_digest}', config, token)
        verify(config, config_digest)
        config_value = json.loads(config.read_text())
        if config_value.get('os') != 'linux' or config_value.get('architecture') != 'amd64':
            raise ValueError('Image configuration has an unexpected platform')
        diff_ids = config_value['rootfs']['diff_ids']
        if len(diff_ids) != len(manifest['layers']):
            raise ValueError('Layer count differs from image configuration')
        layer_names = []
        for index, layer in enumerate(manifest['layers']):
            compressed = work / 'downloaded-layer'
            fetch(f"{base}/blobs/{layer['digest']}", compressed, token)
            verify(compressed, layer['digest'])
            layer_name = f'layers/{index:03d}.tar'
            layer_path = image_files / layer_name
            layer_path.parent.mkdir(exist_ok=True)
            media_type = layer.get('mediaType', '')
            if 'gzip' in media_type:
                with gzip.open(compressed, 'rb') as source, layer_path.open('wb') as target:
                    shutil.copyfileobj(source, target)
            elif media_type.endswith('.tar'):
                shutil.copyfile(compressed, layer_path)
            else:
                raise ValueError(f'Unsupported layer compression: {media_type}')
            verify(layer_path, diff_ids[index])
            layer_names.append(layer_name)
            print(f'  Verified layer {index + 1}/{len(diff_ids)}', flush=True)
        (image_files / 'manifest.json').write_text(json.dumps([
            {'Config': config_name, 'RepoTags': [image], 'Layers': layer_names}
        ]))
        archive_path = work / 'image.tar'
        with tarfile.open(archive_path, 'w') as archive:
            for file in image_files.rglob('*'):
                if file.is_file():
                    archive.add(file, arcname=file.relative_to(image_files).as_posix())
        subprocess.run(['docker', 'load', '--input', str(archive_path)], check=True)


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('images', nargs='+')
    args = parser.parse_args()
    repository_root = pathlib.Path(__file__).resolve().parents[2]
    for requested_image in args.images:
        import_image(requested_image, repository_root / '.tools' / 'image-import')
