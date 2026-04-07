const { WebSocketServer } = require('ws');

const API_BASE_URL = process.env.API_BASE_URL || 'http://api:3001';
const PORT = parseInt(process.env.PORT || '3002', 10);
const MAX_HP = 100;
const PLAYER1_X = 15;
const PLAYER2_X = 85;

const sessions = new Map();
const wss = new WebSocketServer({ port: PORT });

function send(ws, payload) {
  if (ws && ws.readyState === 1) {
    ws.send(JSON.stringify(payload));
  }
}

function sendError(ws, message) {
  send(ws, { type: 'error', message });
}

function broadcast(session, payload) {
  send(session.player1Socket, payload);
  send(session.player2Socket, payload);
}

async function fetchGame(gameId) {
  const response = await fetch(`${API_BASE_URL}/games/${gameId}`);
  if (!response.ok) {
    return null;
  }

  return response.json();
}

function createSession(game) {
  const session = {
    gameId: game.id,
    roomCode: game.room_code,
    player1Id: game.player1_id,
    player2Id: game.player2_id,
    player1Socket: null,
    player2Socket: null,
    player1Hp: MAX_HP,
    player2Hp: MAX_HP,
    player1X: PLAYER1_X,
    player2X: PLAYER2_X,
    currentTurnPlayerId: null,
    status: 'waiting',
    startedAt: null,
    finishedAt: null,
    winnerPlayerId: 0,
    lastShotResult: '',
    lastDamage: 0,
    lastLandingX: 0,
    lastAttackerPlayerId: 0,
    lastAngle: 0,
    lastPower: 0,
  };

  sessions.set(session.gameId, session);
  return session;
}

function getOrCreateSession(game) {
  const existing = sessions.get(game.id);
  if (existing) {
    existing.roomCode = game.room_code;
    existing.player1Id = game.player1_id;
    existing.player2Id = game.player2_id;
    return existing;
  }

  return createSession(game);
}

function attachPlayerSocket(session, playerId, ws) {
  if (playerId === session.player1Id) {
    if (session.player1Socket && session.player1Socket !== ws) {
      session.player1Socket.close();
    }
    session.player1Socket = ws;
  } else if (playerId === session.player2Id) {
    if (session.player2Socket && session.player2Socket !== ws) {
      session.player2Socket.close();
    }
    session.player2Socket = ws;
  }

  ws.sessionGameId = session.gameId;
  ws.playerId = playerId;
}

function detachPlayerSocket(ws) {
  if (!ws.sessionGameId) {
    return;
  }

  const session = sessions.get(ws.sessionGameId);
  if (!session) {
    return;
  }

  if (session.player1Socket === ws) {
    session.player1Socket = null;
  }

  if (session.player2Socket === ws) {
    session.player2Socket = null;
  }

  if (
    !session.player1Socket &&
    !session.player2Socket &&
    session.status === 'finished'
  ) {
    sessions.delete(session.gameId);
  }
}

function buildStatePayload(type, session) {
  return {
    type,
    gameId: session.gameId,
    roomCode: session.roomCode,
    player1Id: session.player1Id,
    player2Id: session.player2Id,
    player1Hp: session.player1Hp,
    player2Hp: session.player2Hp,
    player1X: session.player1X,
    player2X: session.player2X,
    currentTurnPlayerId: session.currentTurnPlayerId,
    winnerPlayerId: session.winnerPlayerId,
    status: session.status,
    lastShotResult: session.lastShotResult,
    lastDamage: session.lastDamage,
    lastLandingX: session.lastLandingX,
    lastAttackerPlayerId: session.lastAttackerPlayerId,
    lastAngle: session.lastAngle,
    lastPower: session.lastPower,
    durationSeconds: session.startedAt
      ? Math.max(
          0,
          Math.floor(((session.finishedAt || Date.now()) - session.startedAt) / 1000)
        )
      : 0,
  };
}

function startSessionIfReady(session) {
  if (!session.player1Socket || !session.player2Socket) {
    const waitingSocket = session.player1Socket || session.player2Socket;
    send(waitingSocket, {
      type: 'joined_waiting',
      gameId: session.gameId,
      roomCode: session.roomCode,
      message: 'Waiting for the other player to connect to combat.',
    });
    return;
  }

  if (!session.startedAt) {
    session.startedAt = Date.now();
    session.status = 'active';
    session.currentTurnPlayerId =
      Math.random() < 0.5 ? session.player1Id : session.player2Id;
  }

  broadcast(session, buildStatePayload('game_start', session));
}

function getTargetInfo(session, attackerPlayerId) {
  if (attackerPlayerId === session.player1Id) {
    return {
      attackerX: session.player1X,
      targetX: session.player2X,
      targetKey: 'player2Hp',
      direction: 1,
    };
  }

  return {
    attackerX: session.player2X,
    targetX: session.player1X,
    targetKey: 'player1Hp',
    direction: -1,
  };
}

function calculateShot(session, attackerPlayerId, angle, power) {
  const { attackerX, targetX, targetKey, direction } = getTargetInfo(
    session,
    attackerPlayerId
  );
  const radians = (angle * Math.PI) / 180;
  const projectedDistance = (power / 100) * 80 * Math.sin(2 * radians);
  const landingX = attackerX + projectedDistance * direction;
  const distanceToTarget = Math.abs(landingX - targetX);

  let result = 'miss';
  let damage = 0;

  if (distanceToTarget <= 4) {
    result = 'direct_hit';
    damage = Math.max(30, Math.min(40, 40 - Math.round(distanceToTarget * 2)));
  } else if (distanceToTarget <= 10) {
    result = 'near_hit';
    damage = Math.max(10, Math.min(20, 20 - Math.round((distanceToTarget - 4) * 1.5)));
  }

  session[targetKey] = Math.max(0, session[targetKey] - damage);
  session.lastShotResult = result;
  session.lastDamage = damage;
  session.lastLandingX = Number(landingX.toFixed(2));
  session.lastAttackerPlayerId = attackerPlayerId;
  session.lastAngle = angle;
  session.lastPower = power;

  return {
    result,
    damage,
    targetHp: session[targetKey],
  };
}

function handleFireShot(ws, payload) {
  const session = sessions.get(ws.sessionGameId);
  if (!session) {
    sendError(ws, 'Game session not found.');
    return;
  }

  if (session.status !== 'active') {
    sendError(ws, 'Game is not active.');
    return;
  }

  const playerId = parseInt(payload.playerId, 10);
  const angle = Math.round(Number(payload.angle));
  const power = Math.round(Number(payload.power));

  if (playerId !== ws.playerId) {
    sendError(ws, 'Player identity mismatch.');
    return;
  }

  if (playerId !== session.currentTurnPlayerId) {
    sendError(ws, 'It is not your turn.');
    return;
  }

  if (!Number.isFinite(angle) || angle < 0 || angle > 90) {
    sendError(ws, 'Angle must be between 0 and 90.');
    return;
  }

  if (!Number.isFinite(power) || power < 0 || power > 100) {
    sendError(ws, 'Power must be between 0 and 100.');
    return;
  }

  calculateShot(session, playerId, angle, power);

  const targetIsDefeated =
    session.player1Hp <= 0 || session.player2Hp <= 0;

  if (targetIsDefeated) {
    session.status = 'finished';
    session.finishedAt = Date.now();
    session.winnerPlayerId = playerId;
  } else {
    session.currentTurnPlayerId =
      playerId === session.player1Id ? session.player2Id : session.player1Id;
  }

  broadcast(session, buildStatePayload('game_update', session));

  if (session.status === 'finished') {
    broadcast(session, buildStatePayload('game_end', session));
  }
}

async function handleJoinGame(ws, payload) {
  const gameId = parseInt(payload.gameId, 10);
  const playerId = parseInt(payload.playerId, 10);

  if (!gameId || !playerId) {
    sendError(ws, 'gameId and playerId are required.');
    return;
  }

  try {
    const game = await fetchGame(gameId);
    if (!game) {
      sendError(ws, 'Game not found.');
      return;
    }

    if (game.player1_id !== playerId && game.player2_id !== playerId) {
      sendError(ws, 'Player is not part of this game.');
      return;
    }

    if (!game.player1_id || !game.player2_id || game.status !== 'in_progress') {
      sendError(ws, 'Game is not ready for combat yet.');
      return;
    }

    const session = getOrCreateSession(game);
    attachPlayerSocket(session, playerId, ws);
    startSessionIfReady(session);
  } catch (error) {
    console.error('join_game failed', error);
    sendError(ws, 'Could not join game.');
  }
}

wss.on('connection', (ws) => {
  send(ws, { type: 'connected', message: 'Game Service running' });

  ws.on('message', async (rawMessage) => {
    try {
      const payload = JSON.parse(rawMessage.toString());

      switch (payload.type) {
        case 'join_game':
          await handleJoinGame(ws, payload);
          break;
        case 'fire_shot':
          handleFireShot(ws, payload);
          break;
        default:
          sendError(ws, 'Unknown message type.');
      }
    } catch (error) {
      console.error('message handling failed', error);
      sendError(ws, 'Invalid message.');
    }
  });

  ws.on('close', () => {
    detachPlayerSocket(ws);
  });
});

console.log(`Game Service running on port ${PORT}`);
