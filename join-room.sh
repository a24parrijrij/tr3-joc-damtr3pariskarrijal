#!/usr/bin/env bash

set -euo pipefail

API_BASE_URL="${API_BASE_URL:-http://localhost/api}"

prompt_value() {
  local label="$1"
  local current_value="$2"

  if [ -n "$current_value" ]; then
    printf '%s [%s]: ' "$label" "$current_value" >&2
  else
    printf '%s: ' "$label" >&2
  fi

  local input
  read -r input

  if [ -n "$input" ]; then
    printf '%s' "$input"
  else
    printf '%s' "$current_value"
  fi
}

USERNAME="${USERNAME:-}"
PASSWORD="${PASSWORD:-}"
ROOM_CODE="${ROOM_CODE:-}"

USERNAME="$(prompt_value "Username" "$USERNAME")"
printf 'Password: ' >&2
read -r -s PASSWORD_INPUT
printf '\n' >&2
if [ -n "$PASSWORD_INPUT" ]; then
  PASSWORD="$PASSWORD_INPUT"
fi
ROOM_CODE="$(prompt_value "Room code" "$ROOM_CODE")"
ROOM_CODE="$(printf '%s' "$ROOM_CODE" | tr '[:lower:]' '[:upper:]')"

if [ -z "$USERNAME" ] || [ -z "$PASSWORD" ] || [ -z "$ROOM_CODE" ]; then
  echo "Username, password, and room code are required."
  exit 1
fi

LOGIN_RESPONSE="$(curl -s -X POST "$API_BASE_URL/auth/login" \
  -H 'Content-Type: application/json' \
  -d "{\"username\":\"$USERNAME\",\"password\":\"$PASSWORD\"}")"

PLAYER_ID="$(printf '%s' "$LOGIN_RESPONSE" | sed -n 's/.*"id":\([0-9][0-9]*\).*/\1/p')"

if [ -z "$PLAYER_ID" ]; then
  echo "Login failed:"
  echo "$LOGIN_RESPONSE"
  exit 1
fi

ROOM_RESPONSE="$(curl -s "$API_BASE_URL/games/room/$ROOM_CODE")"
GAME_ID="$(printf '%s' "$ROOM_RESPONSE" | sed -n 's/.*"id":\([0-9][0-9]*\).*/\1/p')"

if [ -z "$GAME_ID" ]; then
  echo "Room lookup failed:"
  echo "$ROOM_RESPONSE"
  exit 1
fi

JOIN_RESPONSE="$(curl -s -X POST "$API_BASE_URL/games/$GAME_ID/join" \
  -H 'Content-Type: application/json' \
  -d "{\"playerId\":$PLAYER_ID}")"

echo
echo "User: $USERNAME"
echo "Player ID: $PLAYER_ID"
echo "Room code: $ROOM_CODE"
echo "Game ID: $GAME_ID"
echo "Join response:"
echo "$JOIN_RESPONSE"
