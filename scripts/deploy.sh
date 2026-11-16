#!/usr/bin/env bash
# Builds the frontend and backend, uploads the output, and restarts the systemd service.
# Run from Git Bash or any bash shell: bash scripts/deploy.sh
set -euo pipefail

REMOTE="${REMOTE:-wonton@172.232.0.7}"
REMOTE_STAGE="${REMOTE_STAGE:-/tmp/pantrypal}"
REMOTE_APP="${REMOTE_APP:-/opt/pantrypal}"
SERVICE="${SERVICE:-pantrypal}"
REMOTE_OWNER="${REMOTE_OWNER:-pantrypal:pantrypal}"

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
DEPLOY_DIR="$REPO_ROOT/deploy"

# Key-based SSH only: BatchMode fails fast instead of prompting for a password.
SSH_OPTS=(-o BatchMode=yes)
if [[ -n "${SSH_KEY:-}" ]]; then
  SSH_OPTS+=(-i "$SSH_KEY")
fi

echo "Building frontend..."
(cd "$REPO_ROOT/frontend" && npm run build)

echo "Publishing backend to $DEPLOY_DIR..."
rm -rf "$DEPLOY_DIR"
dotnet publish "$REPO_ROOT/backend/backend.csproj" -c Release -o "$DEPLOY_DIR"

echo "Uploading to $REMOTE:$REMOTE_STAGE..."
ssh "${SSH_OPTS[@]}" "$REMOTE" "rm -rf '$REMOTE_STAGE' && mkdir -p '$REMOTE_STAGE'"
tar -C "$DEPLOY_DIR" -cf - . | ssh "${SSH_OPTS[@]}" "$REMOTE" "tar -C '$REMOTE_STAGE' -xf -"

# sudo -n never prompts; the deploy user needs a NOPASSWD sudoers rule on the server.
echo "Installing into $REMOTE_APP and restarting $SERVICE..."
ssh "${SSH_OPTS[@]}" "$REMOTE" "sudo -n bash -s" <<EOF
set -e
systemctl stop '$SERVICE'
rm -rf '${REMOTE_APP:?}'/*
cp -r '$REMOTE_STAGE'/. '$REMOTE_APP'/
chown -R '$REMOTE_OWNER' '$REMOTE_APP'
systemctl start '$SERVICE'
systemctl is-active '$SERVICE'
EOF

echo "Deploy finished."
