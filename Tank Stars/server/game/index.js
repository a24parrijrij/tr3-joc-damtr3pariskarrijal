const { WebSocketServer } = require('ws');

const API_BASE_URL = process.env.API_BASE_URL || 'http://api:3001';
const PORT = parseInt(process.env.PORT || '3002', 10);
const MAX_HP = 100;
const PLAYER1_X = 15;
const PLAYER2_X = 85;
const TERRAIN_RADIUS = 8;
const MAP_TYPES = ['desert', 'snow', 'grassland', 'canyon', 'volcanic'];
const MAP_PRESETS = {
  desert: [34, 36, 39, 44, 49, 53, 56, 58, 57, 54, 48, 43, 39, 37, 36, 38, 43, 50, 58, 63, 66, 65, 61, 54],
  snow: [52, 55, 60, 65, 69, 71, 68, 63, 57, 52, 50, 49, 51, 55, 61, 68, 74, 78, 76, 71, 64, 58, 54, 51],
  grassland: [41, 43, 45, 48, 52, 55, 58, 56, 51, 46, 42, 40, 42, 46, 51, 57, 62, 64, 61, 56, 50, 46, 43, 41],
  canyon: [46, 50, 55, 60, 62, 58, 49, 38, 28, 22, 20, 23, 31, 42, 55, 66, 72, 74, 69, 60, 52, 47, 45, 44],
  volcanic: [39, 42, 46, 52, 60, 70, 78, 82, 74, 61, 48, 39, 35, 37, 45, 57, 68, 76, 73, 64, 53, 46, 41, 38],
};

const sessions = new Map();
const wss = new WebSocketServer({ port: PORT });

function normalizeMapType(mapType) {
  const normalized = (mapType || 'desert').toLowerCase();
  return MAP_TYPES.includes(normalized) ? normalized : 'desert';
}

function cloneTerrainHeights(mapType) {
  return MAP_PRESETS[normalizeMapType(mapType)].slice();
}

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
  const mapType = normalizeMapType(game.map_type);
  const session = {
    gameId: game.id,
    roomCode: game.room_code,
    mapType,
    terrainHeights: cloneTerrainHeights(mapType),
    terrainEventId: 0,
    lastImpactX: 0,
    lastImpactY: 0,
    lastImpactRadius: 0,
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
  const mapType = normalizeMapType(game.map_type);
  const existing = sessions.get(game.id);
  if (existing) {
    existing.roomCode = game.room_code;
    existing.player1Id = game.player1_id;
    existing.player2Id = game.player2_id;
    existing.mapType = mapType;
    if (!existing.terrainHeights || existing.terrainHeights.length === 0) {
      existing.terrainHeights = cloneTerrainHeights(mapType);
    }
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

  if (!session.player1Socket && !session.player2Socket && session.status === 'finished') {
    sessions.delete(session.gameId);
  }
}

function buildStatePayload(type, session) {
  return {
    type,
    gameId: session.gameId,
    roomCode: session.roomCode,
    mapType: session.mapType,
    terrainHeights: session.terrainHeights,
    terrainEventId: session.terrainEventId,
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
    lastImpactX: session.lastImpactX,
    lastImpactY: session.lastImpactY,
    lastImpactRadius: session.lastImpactRadius,
    durationSeconds: session.startedAt
      ? Math.max(0, Math.floor(((session.finishedAt || Date.now()) - session.startedAt) / 1000))
      : 0,
  };
}

function buildTerrainDestroyedPayload(session) {
  return {
    type: 'terrain_destroyed',
    gameId: session.gameId,
    mapType: session.mapType,
    impactX: session.lastImpactX,
    impactY: session.lastImpactY,
    radius: session.lastImpactRadius,
    terrainEventId: session.terrainEventId,
  };
}

function startSessionIfReady(session) {
  if (!session.player1Socket || !session.player2Socket) {
    const waitingSocket = session.player1Socket || session.player2Socket;
    send(waitingSocket, {
      type: 'joined_waiting',
      gameId: session.gameId,
      roomCode: session.roomCode,
      mapType: session.mapType,
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

function getColumnX(index, totalColumns) {
  if (totalColumns <= 1) {
    return 0;
  }

  return (index / (totalColumns - 1)) * 100;
}

function getTerrainHeightAtX(terrainHeights, impactX) {
  if (!Array.isArray(terrainHeights) || terrainHeights.length === 0) {
    return 25;
  }

  let bestIndex = 0;
  let smallestDelta = Infinity;
  for (let index = 0; index < terrainHeights.length; index += 1) {
    const delta = Math.abs(getColumnX(index, terrainHeights.length) - impactX);
    if (delta < smallestDelta) {
      smallestDelta = delta;
      bestIndex = index;
    }
  }

  return terrainHeights[bestIndex];
}

function applyCrater(terrainHeights, impactX, impactY, radius) {
  for (let index = 0; index < terrainHeights.length; index += 1) {
    const columnX = getColumnX(index, terrainHeights.length);
    const deltaX = Math.abs(columnX - impactX);
    if (deltaX > radius) {
      continue;
    }

    const depthFactor = 1 - (deltaX / radius);
    const carvedHeight = Math.round(impactY - (radius * 1.6 * depthFactor));
    terrainHeights[index] = Math.max(6, Math.min(terrainHeights[index], carvedHeight));
  }
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
  session.lastLandingX = Number(Math.max(0, Math.min(100, landingX)).toFixed(2));
  session.lastAttackerPlayerId = attackerPlayerId;
  session.lastAngle = angle;
  session.lastPower = power;

  const impactX = Number(Math.max(0, Math.min(100, landingX)).toFixed(2));
  const impactY = getTerrainHeightAtX(session.terrainHeights, impactX);
  session.lastImpactX = impactX;
  session.lastImpactY = impactY;
  session.lastImpactRadius = TERRAIN_RADIUS;
  session.terrainEventId += 1;
  applyCrater(session.terrainHeights, impactX, impactY, TERRAIN_RADIUS);

  return {
    result,
    damage,
    targetHp: session[targetKey],
  };
}

function handleMoveTank(ws, payload) {
  const session = sessions.get(ws.sessionGameId);
  if (!session || session.status !== 'active') return;

  const playerId = parseInt(payload.playerId, 10);
  if (playerId !== ws.playerId || playerId !== session.currentTurnPlayerId) return;

  const newX = Math.max(5, Math.min(95, Number(payload.newX)));
  if (!Number.isFinite(newX)) return;

  if (playerId === session.player1Id) session.player1X = newX;
  else session.player2X = newX;

  broadcast(session, {
    type: 'positions_update',
    player1X: session.player1X,
    player2X: session.player2X,
  });
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

  if (!MAP_TYPES.includes(session.mapType)) {
    sendError(ws, 'Game map is invalid.');
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
  broadcast(session, buildTerrainDestroyedPayload(session));

  const targetIsDefeated = session.player1Hp <= 0 || session.player2Hp <= 0;
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
    if (!MAP_TYPES.includes(session.mapType)) {
      sendError(ws, 'Selected map type is invalid.');
      return;
    }

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
        case 'move_tank':
          handleMoveTank(ws, payload);
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
