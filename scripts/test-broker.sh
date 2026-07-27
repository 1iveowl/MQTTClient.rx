#!/usr/bin/env bash
#
# Runs a throwaway Mosquitto broker for the MQTTClientRx integration tests.
#
# Only needed when the tests cannot start one themselves — that is, when no Docker daemon is
# reachable from wherever `dotnet test` runs. See tests/README.md.
#
#   ./scripts/test-broker.sh start     # start it, print the export line to copy
#   ./scripts/test-broker.sh stop      # stop and remove it
#   ./scripts/test-broker.sh status    # is it running?
#
set -euo pipefail

NAME="${MQTTCLIENTRX_BROKER_NAME:-mqttclientrx-test-broker}"
PORT="${MQTTCLIENTRX_BROKER_PORT:-1883}"
IMAGE="eclipse-mosquitto:2"

runtime=""
for candidate in docker podman; do
    if command -v "$candidate" >/dev/null 2>&1 && "$candidate" info >/dev/null 2>&1; then
        runtime="$candidate"
        break
    fi
done

if [ -z "$runtime" ]; then
    echo "No working 'docker' or 'podman' found on this machine." >&2
    echo "Run this on the host, not inside the dev container." >&2
    exit 1
fi

case "${1:-start}" in
    start)
        if "$runtime" ps --filter "name=^${NAME}$" --format '{{.Names}}' | grep -q "^${NAME}$"; then
            echo "Already running."
        else
            "$runtime" rm -f "$NAME" >/dev/null 2>&1 || true

            # -c /mosquitto-no-auth.conf ships with the official image and permits anonymous
            # connections, which saves mounting a config file (and the Windows path quoting that
            # would come with it).
            "$runtime" run --detach \
                --name "$NAME" \
                --publish "${PORT}:1883" \
                "$IMAGE" mosquitto -c /mosquitto-no-auth.conf >/dev/null

            echo "Started ${NAME} (${runtime}) on port ${PORT}."
        fi

        echo
        echo "Point the tests at it:"
        echo "  export MQTTCLIENTRX_TEST_BROKER=mqtt://host.docker.internal:${PORT}   # from inside the dev container"
        echo "  export MQTTCLIENTRX_TEST_BROKER=mqtt://localhost:${PORT}              # from the host"
        ;;

    stop)
        "$runtime" rm -f "$NAME" >/dev/null 2>&1 && echo "Stopped ${NAME}." || echo "Not running."
        ;;

    status)
        if "$runtime" ps --filter "name=^${NAME}$" --format '{{.Names}}\t{{.Status}}' | grep -q .; then
            "$runtime" ps --filter "name=^${NAME}$" --format '{{.Names}}\t{{.Status}}'
        else
            echo "Not running."
            exit 1
        fi
        ;;

    *)
        echo "Usage: $0 {start|stop|status}" >&2
        exit 2
        ;;
esac
