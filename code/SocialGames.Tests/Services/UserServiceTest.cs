﻿namespace Microsoft.Samples.SocialGames.Tests
{
    using System;
    using System.Configuration;
    using System.Globalization;
    using System.Linq;
    using System.Net;
    using System.Net.Http;
    using System.Transactions;
    using System.Web.Script.Serialization;
    using Microsoft.Samples.SocialGames;
    using Microsoft.Samples.SocialGames.Common.Storage;
    using Microsoft.Samples.SocialGames.Entities;
    using Microsoft.Samples.SocialGames.Web.Services;
    using Microsoft.Samples.SocialGames.Repositories;
    using Microsoft.Samples.SocialGames.Tests.Mocks;
    using Microsoft.VisualStudio.TestTools.UnitTesting;
    using Microsoft.WindowsAzure;
    using Microsoft.WindowsAzure.ServiceRuntime;

    [TestClass]
    public class UserServiceTest : ServiceTest
    {
        private int suffix;
        private IAzureBlobContainer<UserProfile> userContainer;
        private IAzureBlobContainer<UserSession> userSessionContainer;
        private IAzureBlobContainer<Friends> friendContainer;

        [TestInitialize]
        public void Setup()
        {
            CloudStorageAccount.SetConfigurationSettingPublisher((configName, configSetter) =>
            {
                string configuration = RoleEnvironment.IsAvailable ?
                    RoleEnvironment.GetConfigurationSettingValue(configName) :
                    ConfigurationManager.AppSettings[configName];

                configSetter(configuration);
            });

            this.suffix = (new Random()).Next(10000);
        }

        [TestCleanup]
        public void Teardown()
        {
            if (this.userContainer != null)
            {
                this.userContainer.DeleteContainer();
            }

            if (this.userSessionContainer != null)
            {
                this.userSessionContainer.DeleteContainer();
            }

            if (this.friendContainer != null)
            {
                this.friendContainer.DeleteContainer();
            }
        }

        [TestMethod]
        public void Verify()
        {
            var userId = "UoJw5TuD3UGu9Jd8ct2Fm+tVuo4Xl4fYKvGmT7sldz4=";
            var userRepository = this.CreateUserRepository();
            var statisticsProvider = this.CreateStatisticsRepository();
            var userService = this.CreateUserService(userRepository, statisticsProvider, userId);
            var request = new HttpRequestMessage();

            var response = userService.Verify(request);

            Assert.AreEqual(userId, response.Content.ReadAsStringAsync().Result);
        }

        [TestMethod]
        public void VerifyReturnsErrorIfUserIsNotAuthenticated()
        {
            var userRepository = this.CreateUserRepository();
            var statisticsProvider = this.CreateStatisticsRepository();
            var userService = this.CreateUserService(userRepository, statisticsProvider, string.Empty);
            var request = new HttpRequestMessage();

            var response = userService.Verify(request);

            Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.AreEqual("The user is not authenticated", response.Content.ReadAsStringAsync().Result);
        }

        [TestMethod]
        public void UpdateUserProfileChangeDisplayName()
        {
            var userId = Guid.NewGuid().ToString();
            var userName = "Johnny Anderson";
            var newName = "Johnny New Name";
            var userRepository = this.CreateUserRepository();
            var statisticsProvider = this.CreateStatisticsRepository();
            var userService = this.CreateUserService(userRepository, statisticsProvider, userId);

            var user = new UserProfile { Id = userId, DisplayName = userName };
            userRepository.AddOrUpdateUser(user);

            var parametersTemplate = "displayName={0}";
            var parameters = string.Format(CultureInfo.InvariantCulture, parametersTemplate, newName);
            
            var request = new HttpRequestMessage();
            request.Content = new StringContent(parameters);
            request.Content.Headers.ContentType.MediaType = "application/x-www-form-urlencoded";
            
            userService.UpdateProfile(request);
            user = userRepository.GetUser(userId);

            Assert.AreEqual(userId, user.Id);
            Assert.AreEqual(newName, user.DisplayName);
        }

        [TestMethod]
        public void UpdateUserProfileReturnsErrorIfUserDoesNotExist()
        {
            var userRepository = this.CreateUserRepository();
            var statisticsProvider = this.CreateStatisticsRepository();
            var userService = this.CreateUserService(userRepository, statisticsProvider, "invalid-user");
            var request = new HttpRequestMessage();
            request.Content = new StringContent("displayName=john");
            request.Content.Headers.ContentType.MediaType = "application/x-www-form-urlencoded";

            var response = userService.UpdateProfile(request);

            Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.AreEqual("User does not exist", response.Content.ReadAsStringAsync().Result);
        }

        [TestMethod]
        public void UpdateUserProfileDoesNotChangeIfDisplayNameIsEmpty()
        {
            var userId = Guid.NewGuid().ToString();
            var userName = "Johnny Anderson";
            var userRepository = this.CreateUserRepository();
            var statisticsProvider = this.CreateStatisticsRepository();
            var userService = this.CreateUserService(userRepository, statisticsProvider, userId);

            var user = new UserProfile { Id = userId, DisplayName = userName };
            userRepository.AddOrUpdateUser(user);

            var request = new HttpRequestMessage { Content = new StringContent("displayName=") };
            request.Content.Headers.ContentType.MediaType = "application/x-www-form-urlencoded";

            userService.UpdateProfile(request);
            user = userRepository.GetUser(userId);

            Assert.AreEqual(userId, user.Id);
            Assert.AreEqual(userName, user.DisplayName);
        }

        [TestMethod]
        public void UpdateUserProfileDoesNotChangeIfDisplayNameParameterIsMissing()
        {
            var userId = Guid.NewGuid().ToString();
            var userName = "Johnny Anderson";
            var userRepository = this.CreateUserRepository();
            var statisticsProvider = this.CreateStatisticsRepository();
            var userService = this.CreateUserService(userRepository, statisticsProvider, userId);

            var user = new UserProfile { Id = userId, DisplayName = userName };
            userRepository.AddOrUpdateUser(user);

            var request = new HttpRequestMessage();
            request.Content = new StringContent(string.Empty);
            request.Content.Headers.ContentType.MediaType = "application/x-www-form-urlencoded";

            userService.UpdateProfile(request);
            user = userRepository.GetUser(userId);

            Assert.AreEqual(userId, user.Id);
            Assert.AreEqual(userName, user.DisplayName);
        }

        [TestMethod]
        public void LeaderboardTop10()
        {
            using (var ts = new TransactionScope())
            {
                var userId = "testuser_10";
                var userRepository = this.CreateUserRepository();
                var statisticsProvider = this.CreateStatisticsRepository();

                this.BulkInsertTestData(statisticsProvider);

                var userService = this.CreateUserService(userRepository, statisticsProvider, userId);

                var response = userService.Leaderboard(10);
                var serializer = new JavaScriptSerializer();
                var boards = serializer.Deserialize<Board[]>(response.Content.ReadAsStringAsync().Result);

                Assert.AreEqual(3, boards.Count());

                foreach (var board in boards)
                {
                    Assert.AreEqual(10, board.Scores.Count());
                }
            }
        }

        [TestMethod]
        public void LeaderboardUserFocused()
        {
            using (var ts = new TransactionScope())
            {
                var userId = "testuser_10";
                var userRepository = this.CreateUserRepository();
                 var statisticsProvider = this.CreateStatisticsRepository();

                this.BulkInsertTestData(statisticsProvider);

                var userService = this.CreateUserService(userRepository, statisticsProvider, userId);

                var response = userService.LeaderboardWithFocus(userId, 2);
                var serializer = new JavaScriptSerializer();
                var boards = serializer.Deserialize<Board[]>(response.Content.ReadAsStringAsync().Result);

                Assert.AreEqual(3, boards.Count());

                foreach (var board in boards)
                {
                    Assert.AreEqual(3, board.Scores.Count());
                    Assert.IsNotNull(board.Scores.FirstOrDefault(s => s.UserId == userId));
                }
            }
        }

        [TestMethod]
        public void GetNoFriendsForNewUser()
        {
            var userId = "newUser";
            var userRepository = this.CreateUserRepository();
            var statisticsProvider = this.CreateStatisticsRepository();

            var userService = this.CreateUserService(userRepository, statisticsProvider, userId);

            var response = userService.GetFriends();
            var serializer = new JavaScriptSerializer();
            var friends = serializer.Deserialize<string[]>(response.Content.ReadAsStringAsync().Result);

            Assert.IsNotNull(friends);
            Assert.AreEqual(0, friends.Count());
        }

        [TestMethod]
        public void GetFriends()
        {
            var userId = "newUser";
            var friendId1 = "friend1";
            var friendId2 = "friend2";

            var userRepository = this.CreateUserRepository();
            var statisticsProvider = this.CreateStatisticsRepository();

            userRepository.AddFriend(userId, friendId1);
            userRepository.AddFriend(userId, friendId2);

            var userService = this.CreateUserService(userRepository, statisticsProvider, userId);

            var response = userService.GetFriends();
            var serializer = new JavaScriptSerializer();
            var friends = serializer.Deserialize<string[]>(response.Content.ReadAsStringAsync().Result);

            Assert.IsNotNull(friends);
            Assert.AreEqual(2, friends.Count());
            Assert.IsTrue(friends.Contains(friendId1));
            Assert.IsTrue(friends.Contains(friendId2));
        }

        [TestMethod]
        public void GetFriendsInfo()
        {
            var userId = "newUser";
            var friendId1 = "friend1";
            var friendId2 = "friend2";

            var userRepository = this.CreateUserRepository();
            var statisticsProvider = this.CreateStatisticsRepository();

            userRepository.AddFriend(userId, friendId1);
            userRepository.AddFriend(userId, friendId2);

            var userService = this.CreateUserService(userRepository, statisticsProvider, userId);

            var response = userService.GetFriendsInfo();
            var serializer = new JavaScriptSerializer();
            var friends = serializer.Deserialize<UserInfo[]>(response.Content.ReadAsStringAsync().Result);

            Assert.IsNotNull(friends);
            Assert.AreEqual(2, friends.Count());
            Assert.IsTrue(friends.Any(f => f.Id == friendId1));
            Assert.IsTrue(friends.Any(f => f.Id == friendId2));
            Assert.IsTrue(friends.Any(f => f.DisplayName == friendId1));
            Assert.IsTrue(friends.Any(f => f.DisplayName == friendId2));
        }

        [TestMethod]
        public void GetFriendsInfoWithDisplayName()
        {
            var userId = "newUser";
            var friendId1 = "friend1";
            var friendId2 = "friend2";
            var friendName1 = "Friend One";
            var friendName2 = "Friend Two";

            var userRepository = this.CreateUserRepository();
            var statisticsProvider = this.CreateStatisticsRepository();

            userRepository.AddOrUpdateUser(new UserProfile() { Id = friendId1, DisplayName = friendName1 });
            userRepository.AddOrUpdateUser(new UserProfile() { Id = friendId2, DisplayName = friendName2 });

            userRepository.AddFriend(userId, friendId1);
            userRepository.AddFriend(userId, friendId2);

            var userService = this.CreateUserService(userRepository, statisticsProvider, userId);

            var response = userService.GetFriendsInfo();
            var serializer = new JavaScriptSerializer();
            var friends = serializer.Deserialize<UserInfo[]>(response.Content.ReadAsStringAsync().Result);

            Assert.IsNotNull(friends);
            Assert.AreEqual(2, friends.Count());
            Assert.IsTrue(friends.Any(f => f.Id == friendId1 && f.DisplayName == friendName1));
            Assert.IsTrue(friends.Any(f => f.Id == friendId2 && f.DisplayName == friendName2));
        }

        private void BulkInsertTestData(IStatisticsRepository repository)
        {
            var rnd = new Random();

            for (int i = 0; i < 100; i++)
            {
                var stats = new UserStats()
                {
                    UserId = "testuser_" + i.ToString(),
                    Victories = rnd.Next(1000),
                    Defeats = rnd.Next(1000)
                };

                repository.Save(stats);
            }
        }

        private UserService CreateUserService(IUserRepository userRepository, IStatisticsRepository statisticsProvider, string userId)
        {
            return new UserService(
                userRepository,
                statisticsProvider,
                new StringUserProvider(userId));
        }

        private UserRepository CreateUserRepository()
        {
            var account = CloudStorageAccount.FromConfigurationSetting("DataConnectionString");
            this.userContainer = new AzureBlobContainer<UserProfile>(account, ConfigurationConstants.UsersContainerName + "test" + this.suffix, true);
            this.userSessionContainer = new AzureBlobContainer<UserSession>(account, ConfigurationConstants.UserSessionsContainerName + "test" + this.suffix, true);
            this.friendContainer = new AzureBlobContainer<Friends>(account, ConfigurationConstants.FriendsContainerName + "test" + this.suffix, true);

            this.userContainer.EnsureExist();
            this.userSessionContainer.EnsureExist(true);
            this.friendContainer.EnsureExist(true);

            return new UserRepository(this.userContainer, this.userSessionContainer, this.friendContainer);
        }

        private StatisticsRepository CreateStatisticsRepository()
        {
            return new StatisticsRepository("Data Source=.\\SQLEXPRESS;Initial Catalog=SocialGames;Integrated Security=True");
        }
    }
}