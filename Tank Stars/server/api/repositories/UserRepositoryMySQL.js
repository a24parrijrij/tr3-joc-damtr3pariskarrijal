const UserRepository = require('./UserRepository');
const db = require('../db');

class UserRepositoryMySQL extends UserRepository {
  async findByUsername(username) {
    const [rows] = await db.query('SELECT * FROM users WHERE username = ?', [username]);
    return rows[0] || null;
  }
  async create(username, hashedPassword) {
    const [result] = await db.query(
      'INSERT INTO users (username, password) VALUES (?, ?)',
      [username, hashedPassword]
    );
    return result.insertId;
  }
}
module.exports = UserRepositoryMySQL;