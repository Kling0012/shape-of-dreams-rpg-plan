#!/usr/bin/env python3
"""Export literal English arguments from new Txt(...) and Loc.T(...)."""
import argparse
import fnmatch
import json
import re
from itertools import islice
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
ESCAPES = {'0': '\0', 'a': '\a', 'b': '\b', 'f': '\f', 'n': '\n',
           'r': '\r', 't': '\t', 'v': '\v', '\\': '\\', '"': '"', "'": "'"}


def quoted(source, start):
    """Skip one C# string, including expressions inside interpolated strings."""
    i = start
    while i < len(source) and source[i] in '@$':
        i += 1
    prefix = source[start:i]
    verbatim, interpolated = '@' in prefix, '$' in prefix
    i += 1
    value = []
    while i < len(source):
        c = source[i]
        if c == '"':
            if verbatim and source[i:i + 2] == '""':
                value.append('"')
                i += 2
                continue
            return i + 1, ('other', '') if interpolated else ('string', ''.join(value))
        if not verbatim and c == '\\':
            i += 1
            escape = source[i]
            if escape in 'uUx':
                digits = 8 if escape == 'U' else 4
                match = re.match('[0-9a-fA-F]{1,' + str(digits) + '}', source[i + 1:])
                if not match or (escape != 'x' and len(match[0]) != digits):
                    raise ValueError('Invalid C# Unicode escape')
                value.append(chr(int(match[0], 16)))
                i += 1 + len(match[0])
            else:
                value.append(ESCAPES[escape])
                i += 1
            continue
        if interpolated and c == '{':
            if source[i:i + 2] == '{{':
                i += 2
                continue
            depth = 1
            i += 1
            while i < len(source) and depth:
                if source.startswith('//', i):
                    end = source.find('\n', i)
                    i = len(source) if end < 0 else end
                elif source.startswith('/*', i):
                    i = source.index('*/', i + 2) + 2
                elif source[i] == '"' or (source[i] in '@$' and re.match(r'[@$]{1,2}"', source[i:])):
                    i, _ = quoted(source, i)
                elif source[i] == "'":
                    i = char_end(source, i)
                else:
                    depth += (source[i] == '{') - (source[i] == '}')
                    i += 1
            continue
        value.append(c)
        i += 1
    raise ValueError('Unterminated C# string')


def char_end(source, i):
    i += 1
    while i < len(source):
        if source[i] == '\\':
            i += 2
        elif source[i] == "'":
            return i + 1
        else:
            i += 1
    raise ValueError('Unterminated C# character')


def tokens(source):
    result = []
    i = 0
    while i < len(source):
        c = source[i]
        if c.isspace():
            i += 1
        elif source.startswith('//', i):
            end = source.find('\n', i)
            i = len(source) if end < 0 else end
        elif source.startswith('/*', i):
            i = source.index('*/', i + 2) + 2
        elif c == '"' or (c in '@$' and re.match(r'[@$]{1,2}"', source[i:])):
            i, token = quoted(source, i)
            result.append(token)
        elif c == "'":
            i = char_end(source, i)
            result.append(('other', ''))
        elif c.isalpha() or c == '_':
            match = re.match(r'\w+', source[i:])
            result.append(('id', match[0]))
            i += len(match[0])
        else:
            result.append(('punct', c))
            i += 1
    return result


def english_literals(source):
    stream = tokens(source)
    for i in range(len(stream) - 3):
        head = stream[i:i + 4]
        if head[:3] == [('id', 'new'), ('id', 'Txt'), ('punct', '(')]:
            start = i + 3
        elif head == [('id', 'Loc'), ('punct', '.'), ('id', 'T'), ('punct', '(')]:
            start = i + 4
        else:
            continue
        depth, argument, current = 0, 0, []
        for kind, value in islice(stream, start, None):
            if kind == 'punct' and value in '([{':
                depth += 1
            elif kind == 'punct' and value in ')]}':
                if depth == 0:
                    if argument == 1 and len(current) == 1 and current[0][0] == 'string':
                        yield current[0][1]
                    break
                depth -= 1
            if kind == 'punct' and value == ',' and depth == 0:
                if argument == 1:
                    if len(current) == 1 and current[0][0] == 'string':
                        yield current[0][1]
                    break
                argument += 1
                current = []
            else:
                current.append((kind, value))


def exclusions(path):
    files, texts = [], []
    for number, raw in enumerate(path.read_text(encoding='utf-8').splitlines(), 1):
        line = raw.strip()
        if not line or line.startswith('#'):
            continue
        kind, separator, pattern = line.partition(':')
        if kind == 'file' and separator:
            files.append(pattern.strip())
        elif kind == 'text' and separator:
            texts.append(re.compile(pattern.strip()))
        else:
            raise ValueError(f'{path}:{number}: expected file:glob or text:regex')
    return files, texts


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--version', required=True)
    parser.add_argument('--out', required=True, type=Path)
    args = parser.parse_args()
    files, texts = exclusions(Path(__file__).with_name('exclude.txt'))
    keys = set()
    for path in sorted((ROOT / 'src').rglob('*.cs')):
        relative = path.relative_to(ROOT).as_posix()
        if any(fnmatch.fnmatchcase(relative, pattern) for pattern in files):
            continue
        try:
            for text in english_literals(path.read_text(encoding='utf-8-sig')):
                if text and not any(pattern.search(text) for pattern in texts):
                    keys.add(text)
        except (ValueError, KeyError, IndexError) as error:
            raise SystemExit(f'{relative}: {error}') from error
    result = {'version': args.version, 'count': len(keys),
              'strings': {key: '' for key in sorted(keys)}}
    args.out.parent.mkdir(parents=True, exist_ok=True)
    args.out.write_text(json.dumps(result, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    print(f'Exported {len(keys)} strings to {args.out}')


if __name__ == '__main__':
    main()
