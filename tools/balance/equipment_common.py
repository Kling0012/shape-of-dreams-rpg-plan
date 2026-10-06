"""Shared equipment JSON validation and legacy semantic content identity."""
import json
from decimal import Decimal
from pathlib import Path


def _unique(pairs):
    result = {}
    for key, value in pairs:
        if key in result:
            raise ValueError(f"duplicate JSON key: {key}")
        result[key] = value
    return result


def load_json(path):
    def invalid(value):
        raise ValueError(f"{path}: non-finite number {value}")
    return json.loads(Path(path).read_text(encoding="utf-8"), parse_float=Decimal,
                      parse_constant=invalid, object_pairs_hook=_unique)


def quote(value):
    return json.dumps(value, ensure_ascii=False)


def semantic_identity(records):
    """Existing content FNV-1a over ordered UTF-16 records; not a file hash."""
    ordered = sorted(records)
    value = 1469598103934665603
    for record in ordered:
        encoded = record.encode("utf-16-le")
        for index in range(0, len(encoded), 2):
            value ^= encoded[index] | encoded[index + 1] << 8
            value = value * 1099511628211 & ((1 << 64) - 1)
        value = (value ^ 10) * 1099511628211 & ((1 << 64) - 1)
    return f"{len(ordered)}-{value:016x}"


def fingerprint_records(domain, records, legacy_identity):
    """Preserve pre-cutover negotiation identity; changed content adds typed records."""
    if semantic_identity(records) == legacy_identity:
        return []
    return [f"balance:equipment:{domain}:v1:{record}" for record in sorted(records)]
