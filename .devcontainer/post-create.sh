#!/usr/bin/env bash
set -euo pipefail

solution_path="MQTTClient.rx.slnx"
integration_broker_image="eclipse-mosquitto:2"

# Docker creates named volumes as root. Make persistent tool state available to
# the non-root account required for Claude's bypass-permissions mode.
sudo chown -R "$(id -u):$(id -g)" "${CLAUDE_CONFIG_DIR}" "${CODEX_HOME}" "${COPILOT_HOME}"

skills_installer="/opt/ai-skill/scripts/install-macos.sh"
if [[ -f "${skills_installer}" ]]; then
	bash "${skills_installer}" --no-prompt
else
	echo "Shared skills installer is unavailable at ${skills_installer}; skipping skill installation." >&2
fi

if [[ -f "${solution_path}" ]]; then
	# Warm the NuGet cache so the first build in a fresh container is not also the first restore.
	# Report failures without aborting the rest of the container setup.
	if ! dotnet restore "${solution_path}"; then
		echo "dotnet restore reported errors for ${solution_path}; continuing container setup." >&2
	fi
else
	echo "${solution_path} has not been created yet; skipping restore."
fi

# Pre-pull the broker image the integration tests spin up, so the first `dotnet test` is not also
# an image download. Entirely optional: Testcontainers pulls on demand if this is skipped.
if command -v docker >/dev/null 2>&1 && docker info >/dev/null 2>&1; then
	if ! docker image inspect "${integration_broker_image}" >/dev/null 2>&1; then
		docker pull "${integration_broker_image}" >/dev/null 2>&1 \
			|| echo "Could not pre-pull ${integration_broker_image}; tests will pull it on demand." >&2
	fi
else
	echo "No Docker daemon yet; integration tests will skip until one is available." >&2
fi

npm install -g --allow-scripts=@anthropic-ai/claude-code @anthropic-ai/claude-code @openai/codex

claude_settings="${CLAUDE_CONFIG_DIR}/settings.json"
mkdir -p "${CLAUDE_CONFIG_DIR}" "${CODEX_HOME}"

# The JavaScript template literal is intentionally protected from shell expansion.
# shellcheck disable=SC2016
node -e '
const fs = require("fs");
const path = process.argv[1];
let settings = {};
try { settings = JSON.parse(fs.readFileSync(path, "utf8")); } catch (error) {
  if (error.code !== "ENOENT") throw error;
}
settings.defaultMode = "bypassPermissions";
fs.writeFileSync(path, `${JSON.stringify(settings, null, 2)}\n`);
' "${claude_settings}"

codex_config="${CODEX_HOME}/config.toml"
touch "${codex_config}"
sed -i -E '/^(approval_policy|sandbox_mode)[[:space:]]*=/d' "${codex_config}"
printf '\napproval_policy = "never"\nsandbox_mode = "danger-full-access"\n' >> "${codex_config}"
