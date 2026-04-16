const express = require('express');
const cors    = require('cors');
const app     = express();
const db      = require('./db');

app.use(cors());
app.use(express.json());

// Routes
const authRoutes  = require('./routes/auth');
const gameRoutes  = require('./routes/games');
const resultRoutes = require('./routes/results');

app.use('/auth',   authRoutes);
app.use('/games',  gameRoutes);
app.use('/results', resultRoutes);

// Health check
app.get('/health', (req, res) => {
    res.json({ status: 'API running' });
});

async function start() {
    try {
        await db.ensureSchema();
        app.listen(3001, () => {
            console.log('API Service running on port 3001');
        });
    } catch (error) {
        console.error('API startup failed', error);
        process.exit(1);
    }
}

start();
