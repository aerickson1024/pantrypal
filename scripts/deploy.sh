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

read -r -s -p "Password for $REMOTE (used for SSH and sudo): " DEPLOY_PW
echo
export DEPLOY_PW

# ssh calls this helper for its password prompts instead of asking again.
ASKPASS_SCRIPT="$(mktemp)"
trap 'rm -f "$ASKPASS_SCRIPT"' EXIT
cat > "$ASKPASS_SCRIPT" <<'EOF'
#!/usr/bin/env bash
printf '%s\n' "$DEPLOY_PW"
EOF
chmod +x "$ASKPASS_SCRIPT"
export SSH_ASKPASS="$ASKPASS_SCRIPT" SSH_ASKPASS_REQUIRE=force

echo "Building frontend..."
(cd "$REPO_ROOT/frontend" && npm run build)

echo "Publishing backend to $DEPLOY_DIR..."
rm -rf "$DEPLOY_DIR"
dotnet publish "$REPO_ROOT/backend/backend.csproj" -c Release -o "$DEPLOY_DIR"

echo "Uploading to $REMOTE:$REMOTE_STAGE..."
ssh "$REMOTE" "rm -rf '$REMOTE_STAGE' && mkdir -p '$REMOTE_STAGE'"
tar -C "$DEPLOY_DIR" -cf - . | ssh "$REMOTE" "tar -C '$REMOTE_STAGE' -xf -"

# The password is piped to sudo -S; the remote commands follow on the same stdin for bash -s.
echo "Installing into $REMOTE_APP and restarting $SERVICE..."
{
  printf '%s\n' "$DEPLOY_PW"
  cat <<EOF
set -e
systemctl stop '$SERVICE'
rm -rf '${REMOTE_APP:?}'/*
cp -r '$REMOTE_STAGE'/. '$REMOTE_APP'/
chown -R '$REMOTE_OWNER' '$REMOTE_APP'
systemctl start '$SERVICE'
systemctl is-active '$SERVICE'
EOF
} | ssh "$REMOTE" "sudo -S -p '' bash -s"
unset DEPLOY_PW

echo "Deploy finished."
