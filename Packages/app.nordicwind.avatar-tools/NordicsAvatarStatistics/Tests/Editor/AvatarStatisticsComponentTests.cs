using NUnit.Framework;
using UnityEngine;
using NordicWind.AvatarStatistics;

namespace NordicWind.AvatarStatistics.Tests
{
    public sealed class AvatarStatisticsComponentTests
    {
        [Test]
        public void MarkerCanBeAddedToAvatarRoot()
        {
            var root = new GameObject("Avatar");
            try
            {
                Assert.IsNotNull(root.AddComponent<NordicsAvatarStatistics>());
            }
            finally { Object.DestroyImmediate(root); }
        }
    }
}
