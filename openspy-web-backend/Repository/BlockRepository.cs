using CoreWeb.Controllers.Presence;
using CoreWeb.Database;
using CoreWeb.Models;
using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System;
using RabbitMQ.Client;

namespace CoreWeb.Repository
{
    public class BlockRepository : IRepository<Block, BuddyLookup>
    {
        private GameTrackerDBContext gameTrackerDb;
        private IRepository<User, UserLookup> userRepository;
        private IRepository<Profile, ProfileLookup> profileRepository;
        private IMQConnectionFactory connectionFactory;
        private String GP_EXCHANGE;
        private String GP_BLOCK_ROUTING_KEY;

        public BlockRepository(GameTrackerDBContext gameTrackerDb, IRepository<User, UserLookup> userRepository, IRepository<Profile, ProfileLookup> profileRepository, IMQConnectionFactory connectionFactory)
        {
            GP_EXCHANGE = "presence.core";
            GP_BLOCK_ROUTING_KEY = "presence.buddies";

            this.userRepository = userRepository;
            this.profileRepository = profileRepository;
            this.gameTrackerDb = gameTrackerDb;
            this.connectionFactory = connectionFactory;
        }
        public async Task<IEnumerable<Block>> Lookup(BuddyLookup lookup)
        {
            var query = gameTrackerDb.Block as IQueryable<Block>;
            var from_profile = (await this.profileRepository.Lookup(lookup.SourceProfile)).First();
            query = query.Where(b => b.FromProfileid == from_profile.Id);
            return await query.ToListAsync();
        }
        public Task<bool> Delete(BuddyLookup lookup)
        {
            return Task.Run(async () =>
            {
                var buddies = (await Lookup(lookup)).ToList();
                foreach (var Block in buddies)
                {
                    gameTrackerDb.Remove<Block>(Block);
                }
                var num_modified = await gameTrackerDb.SaveChangesAsync();
                return buddies.Count > 0 && num_modified > 0;
            });
        }
        public Task<Block> Update(Block model)
        {
            throw new NotImplementedException();
        }

        public async Task<Block> Create(Block model)
        {
            var entry = await gameTrackerDb.AddAsync<Block>(model);
            var num_modified = await gameTrackerDb.SaveChangesAsync();
            return entry.Entity;
        }
        public async Task SendAddEvent(Profile from, Profile to)
        {
            await SendBlockEventAsync("block_buddy", from, to);
        }
        public async Task SendDeleteEvent(Profile from, Profile to)
        {
            await SendBlockEventAsync("del_block_buddy", from, to);
        }
        private async Task SendBlockEventAsync(String type, Profile from, Profile to)
        {
            ConnectionFactory factory = connectionFactory.Get();
            using (var connection = await factory.CreateConnectionAsync())
            {
                using (var channel = await connection.CreateChannelAsync())
                {
                    String message = String.Format("\\type\\{0}\\from_profileid\\{1}\\to_profileid\\{2}", type, from.Id, to.Id);
                    byte[] messageBodyBytes = System.Text.Encoding.UTF8.GetBytes(message);

                    var props = new BasicProperties();
                    props.ContentType = "text/plain";
                    await channel.BasicPublishAsync(GP_EXCHANGE, GP_BLOCK_ROUTING_KEY, true, props, messageBodyBytes);
                }
            }
        }
    }
}
