class UserRepository {
  async findByUsername(username) { throw new Error('Not implemented'); }
  async create(username, hashedPassword) { throw new Error('Not implemented'); }
}
module.exports = UserRepository;