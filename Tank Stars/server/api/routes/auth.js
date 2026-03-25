const express = require('express');
const router = express.Router();
const bcrypt = require('bcrypt');
const jwt = require('jsonwebtoken');
const UserRepositoryMySQL = require('../repositories/UserRepositoryMySQL');

const userRepo = new UserRepositoryMySQL();
const SECRET = process.env.JWT_SECRET || 'tankstars_secret';

// REGISTER
router.post('/register', async (req, res) => {
  const { username, password } = req.body;
  if (!username || !password)
    return res.status(400).json({ error: 'Missing fields' });
  try {
    const existing = await userRepo.findByUsername(username);
    if (existing) return res.status(400).json({ error: 'Username already exists' });
    const hash = await bcrypt.hash(password, 10);
    await userRepo.create(username, hash);
    res.json({ message: 'User registered successfully' });
  } catch (err) {
    res.status(500).json({ error: 'Server error' });
  }
});

// LOGIN
router.post('/login', async (req, res) => {
  const { username, password } = req.body;
  try {
    const user = await userRepo.findByUsername(username);
    if (!user) return res.status(401).json({ error: 'Invalid credentials' });
    const match = await bcrypt.compare(password, user.password);
    if (!match) return res.status(401).json({ error: 'Invalid credentials' });
    const token = jwt.sign(
      { id: user.id, username: user.username },
      SECRET,
      { expiresIn: '24h' }
    );
    res.json({ token, username: user.username, id: user.id });
  } catch (err) {
    res.status(500).json({ error: 'Server error' });
  }
});

module.exports = router;