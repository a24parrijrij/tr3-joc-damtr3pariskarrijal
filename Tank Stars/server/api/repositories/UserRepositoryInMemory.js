const UserRepository = require('./UserRepository');

class UserRepositoryInMemory extends UserRepository {
  constructor() {
    super();
    this.users = [];
    this.nextId = 1;
  }
  async findByUsername(username) {
    return this.users.find(u => u.username === username) || null;
  }
  async create(username, hashedPassword) {
    const user = { id: this.nextId++, username, password: hashedPassword };
    this.users.push(user);
    return user.id;
  }
}
module.exports = UserRepositoryInMemory;