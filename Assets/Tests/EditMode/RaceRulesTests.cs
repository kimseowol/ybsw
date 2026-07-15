// PRD-001
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace Ybsw.Racing.Tests
{
    public sealed class RaceRulesTests
    {
        [Test]
        public void CountdownDuration_IsFiveSeconds()
        {
            Assert.That(RaceRules.CountdownDurationInSeconds, Is.EqualTo(5d));
        }

        [TestCase(0, false)]
        [TestCase(1, true)]
        [TestCase(2, true)]
        [TestCase(4, true)]
        public void CanStart_AllowsImmediateSoloStart(int playerCount, bool expected)
        {
            Assert.That(RaceRules.CanStart(playerCount), Is.EqualTo(expected));
        }

        [TestCase(0, true)]
        [TestCase(3, true)]
        [TestCase(4, false)]
        [TestCase(5, false)]
        public void CanApproveConnection_RejectsPlayersBeyondCapacity(int occupiedSlots, bool expected)
        {
            Assert.That(RaceRules.CanApproveConnection(occupiedSlots), Is.EqualTo(expected));
        }

        [TestCase(0, 1)]
        [TestCase(1, 2)]
        [TestCase(3, 4)]
        public void GetNextPlace_ReturnsOneBasedPlacement(int finishedPlayers, int expectedPlace)
        {
            Assert.That(RaceRules.GetNextPlace(finishedPlayers), Is.EqualTo(expectedPlace));
        }

        [Test]
        public void RaceLauncher_DisableReleasesConnectionApprovalCallback()
        {
            var gameObject = new GameObject("RaceLauncherLifecycleTest");

            try
            {
                RaceLauncher launcher = gameObject.AddComponent<RaceLauncher>();
                const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
                MethodInfo awake = typeof(RaceLauncher).GetMethod("Awake", Flags);
                MethodInfo onEnable = typeof(RaceLauncher).GetMethod("OnEnable", Flags);
                MethodInfo onDisable = typeof(RaceLauncher).GetMethod("OnDisable", Flags);
                FieldInfo networkManagerField = typeof(RaceLauncher).GetField("networkManager", Flags);

                awake.Invoke(launcher, null);
                onEnable.Invoke(launcher, null);

                object networkManager = networkManagerField.GetValue(launcher);
                PropertyInfo callbackProperty = networkManager.GetType().GetProperty(
                    "ConnectionApprovalCallback");

                Assert.That(callbackProperty.GetValue(networkManager), Is.Not.Null);

                onDisable.Invoke(launcher, null);

                Assert.That(callbackProperty.GetValue(networkManager), Is.Null);
            }
            finally
            {
                Object.DestroyImmediate(gameObject);
            }
        }
    }
}
