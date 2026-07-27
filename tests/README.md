# Running the tests

```shell
dotnet test
```

That is the whole story for most changes. Of the 149 tests, **138 need nothing but the SDK** - the
reactive semantics, the reconnect curve in virtual time, option validation, protocol mapping, the
pull-based `ReadAllAsync` bridge, and the connection-failure paths.

The remaining **11 need a broker**. They cover the things no mock can establish: that subscribing
really opens a TCP connection, that QoS 1 and 2 round-trip, that MQTT 5 properties survive the wire,
that 3.1.1 degrades cleanly, and that a dropped connection recovers.

Without a broker they **skip**, and `dotnet test` still passes. That is intentional - a green run on
a laptop without Docker means "everything I could check passed", not "everything passed".

---

## The three modes

The test harness picks one automatically at startup.

| Mode | When | Coverage |
|---|---|---|
| **Managed** | A Docker daemon is reachable | All 149. The dev container default, and what CI runs. |
| **External** | `MQTTCLIENTRX_TEST_BROKER` is set | 147 - the 2 that restart the broker skip |
| **None** | Neither | 138 |

### Managed - the default

Testcontainers starts a disposable `eclipse-mosquitto:2` per run and throws it away afterwards.
Nothing to install, nothing left behind, and it is exactly what CI does.

The dev container enables this through the `docker-in-docker` feature, so a fresh container needs no
setup - `dotnet test` runs all 149 in a few seconds. Confirm the daemon is up with:

```shell
docker info >/dev/null && echo "managed mode available"
```

If that fails, the container predates the feature: **Dev Containers: Rebuild Container**.

> The feature is deliberately `docker-in-docker` rather than `docker-outside-of-docker`. With the
> latter, Testcontainers starts the broker as a *sibling* of the dev container, so the mapped port
> lands on the host's loopback rather than ours and every connection is refused until you also set
> `TESTCONTAINERS_HOST_OVERRIDE`. Nested Docker just works.

### External - a broker you already run

For a broker that lives outside this repository entirely, or when nested Docker is unavailable:

```shell
# On the host, outside the dev container:
./scripts/test-broker.sh start
```

```shell
# In the dev container, where the tests run:
export MQTTCLIENTRX_TEST_BROKER=mqtt://host.docker.internal:1883
dotnet test
```

`host.docker.internal` resolves to the host from inside a container on Docker Desktop. On plain Linux
Docker use the host's LAN address, or add
`"runArgs": ["--add-host=host.docker.internal:host-gateway"]` to `devcontainer.json`.

The script wraps one command, if you would rather run it directly:

```shell
docker run -d --name mqttclientrx-test-broker -p 1883:1883 \
    eclipse-mosquitto:2 mosquitto -c /mosquitto-no-auth.conf
```

`./scripts/test-broker.sh stop` when you are done. It also accepts `status`, and honours
`MQTTCLIENTRX_BROKER_PORT` if 1883 is taken.

**Two tests skip in this mode**, both marked `[ControllableBrokerFact]`:

- `Reconnects_and_resubscribes_after_the_broker_restarts`
- `A_disconnect_is_reported_with_ClientWasConnected_set`

They stop and start the broker mid-test, which is only reasonable against one the test run started
itself. These are the two most valuable integration tests in the suite - they are the only proof
that the reconnect layer replacing MQTTnet's removed ManagedClient actually works - so if you are
touching reconnection, use the managed mode.

---

## Why not a public broker?

`test.mosquitto.org` and friends are the obvious shortcut, and the samples do use them. For the test
suite they are the wrong tool:

- **The two reconnect tests become impossible.** You cannot restart somebody else's broker, and those
  are precisely the tests worth having.
- **Shared topics, shared traffic.** Wildcard subscriptions pick up strangers' messages, so
  assertions on message counts stop meaning anything.
- **Rate limits and outages** turn a red build into a coin toss, which trains people to ignore red
  builds.
- **Etiquette.** Pointing CI at a volunteer-run public broker is not a reasonable thing to do to it.

A local broker starts in about a second and has none of these problems.

---

## Filtering

```shell
# Unit tests only - never touches a broker
dotnet test --filter "FullyQualifiedName!~BrokerIntegrationTests"

# Integration tests only
dotnet test --filter "FullyQualifiedName~BrokerIntegrationTests"
```

## Writing new tests

- Timing behaviour belongs in virtual time. Rx operators take an `IScheduler` (`TestScheduler`);
  `CancellationTokenSource` timeouts take a `TimeProvider` (`FakeTimeProvider`). Never `Task.Delay`
  your way through a timing assertion.
- Needs a broker → `[BrokerFact]` / `[BrokerTheory]`.
- Needs to stop or start the broker → `[ControllableBrokerFact]`.
- Needs neither → plain `[Fact]`. Check this before reaching for a broker attribute; a test gated on
  infrastructure it does not use is coverage you silently lose.
