const express = require('express');
const router = express.Router();
const db = require('../db');

router.post('/', async (req, res) => {
  const { gameId, winnerId, loserId, winnerHp, durationSeconds } = req.body;
  
  if (!gameId || !winnerId || !loserId)
    return res.status(400).json({ error: 'Missing required fields' });
    
  try {
    await db.query(
      'INSERT INTO results (game_id, winner_id, loser_id, winner_hp, duration_seconds) VALUES (?,?,?,?,?)',
      [gameId, winnerId, loserId, winnerHp || 0, durationSeconds || 0]
    );
    await db.query('UPDATE users SET wins = wins + 1 WHERE id = ?', [winnerId]);
    await db.query('UPDATE users SET losses = losses + 1 WHERE id = ?', [loserId]);
    res.json({ message: 'Resultat guardat' });
  } catch (err) {
    console.error('Error guardant resultat:', err);
    res.status(500).json({ error: 'Error del servidor' });
  }
});

module.exports = router;
