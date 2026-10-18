#!/usr/bin/env bash
set +x
umask 077
root="$(git -C "$(dirname "$0")" rev-parse --show-toplevel 2>/dev/null)" || exit 1
logs="${CI_LOG_DIR:-$root/.ci-logs}"
mkdir -p "$logs" 2>/dev/null || exit 1
number="${1:-0}"
[[ "$number" =~ ^[1-9][0-9]*$ ]] || number=0
python3 "$root/tools/ci/run-shard.py" "$number" >"$logs/driver-$number.log" 2>&1
result=$?
summary=""
while IFS= read -r line; do
    [[ "$line" =~ ^shard\ [0-9]+:\ (ok|fail),\ passed\ [0-9]+,\ failed\ [0-9]+$ ]] && summary="$line"
done <"$logs/driver-$number.log"
printf '%s\n' "${summary:-shard $number: fail, passed 0, failed 0}"
exit "$result"
