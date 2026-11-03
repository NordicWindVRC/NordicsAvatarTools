using System;
using System.Linq;
using NUnit.Framework;
using NordicWind.AvatarStatistics.Editor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace NordicWind.AvatarStatistics.Tests
{
    public sealed class AvatarStatisticsCalculationTests
    {
        private static PerformanceMetric Metric(double value, string name = "Triangles") => new PerformanceMetric
        {
            Name = name, Value = value, PcValue = value, QuestValue = value,
            Pc = new double[] { 10, 20, 30, 40 }, Quest = new double[] { 1, 2, 3, 4 },
        };

        [TestCase(5, 0)]
        [TestCase(10, 0)]
        [TestCase(15, 1)]
        [TestCase(40, 3)]
        [TestCase(45, 4)]
        public void RankUsesInclusivePublishedMaxima(double value, int expected)
        {
            Assert.That(AvatarStatisticsInfo.Rank(Metric(value), false), Is.EqualTo(expected));
        }

        [Test]
        public void RankUsesQuestThresholdsForQuest()
        {
            Assert.That(AvatarStatisticsInfo.Rank(Metric(3), true), Is.EqualTo(2));
        }

        [Test]
        public void UnavailableMetricHasNoRank()
        {
            var metric = Metric(5);
            metric.Available = false;
            Assert.That(AvatarStatisticsInfo.Rank(metric, false), Is.EqualTo(-1));
        }

        [Test]
        public void ComponentsRemovedOnQuestHaveNoQuestRank()
        {
            Assert.That(AvatarStatisticsInfo.Rank(Metric(1, "Lights"), true), Is.EqualTo(-1));
            Assert.That(AvatarStatisticsInfo.Rank(Metric(1, "Lights"), false), Is.EqualTo(0));
        }

        [TestCase(2f, 2f, 2f, 0)]
        [TestCase(3f, 1f, 1f, 1)]
        [TestCase(4.5f, 5.5f, 1f, 2)]
        [TestCase(6f, 1f, 1f, 4)]
        public void BoundsRankChecksEveryAxis(float x, float y, float z, int expected)
        {
            var metric = new PerformanceMetric { Name = "Bounds", IsBounds = true, BoundsX = x, BoundsY = y, BoundsZ = z };
            Assert.That(AvatarStatisticsInfo.Rank(metric, false), Is.EqualTo(expected));
        }

        [Test]
        public void QuestHardLimitsMatchPublishedValues()
        {
            Assert.That(AvatarStatisticsInfo.QuestHardLimit(Metric(0, "PhysBones Components")), Is.EqualTo(8));
            Assert.That(AvatarStatisticsInfo.QuestHardLimit(Metric(0, "Triangles")), Is.Null);
        }

        // The SDK types are created by reflection, like the collector reads them, so this
        // test assembly does not need a compile-time VRChat SDK reference.
        [Test]
        public void SyncedParameterBitsMatchTheSdk()
        {
            var parametersType = FindType("VRC.SDK3.Avatars.ScriptableObjects.VRCExpressionParameters");
            var descriptorType = FindType("VRC.SDK3.Avatars.Components.VRCAvatarDescriptor");
            if (parametersType == null || descriptorType == null) Assert.Ignore("VRChat Avatars SDK not installed.");
            var parameterType = parametersType.GetNestedType("Parameter");
            var valueType = parametersType.GetNestedType("ValueType");
            var avatar = new GameObject("Avatar");
            var parameters = ScriptableObject.CreateInstance(parametersType);
            try
            {
                var list = Array.CreateInstance(parameterType, 4);
                (string name, string type, bool synced)[] specs =
                    { ("Toggle", "Bool", true), ("Outfit", "Int", true), ("Hue", "Float", true), ("Local", "Float", false) };
                for (int i = 0; i < specs.Length; i++)
                {
                    var p = Activator.CreateInstance(parameterType);
                    parameterType.GetField("name").SetValue(p, specs[i].name);
                    parameterType.GetField("valueType").SetValue(p, Enum.Parse(valueType, specs[i].type));
                    parameterType.GetField("networkSynced").SetValue(p, specs[i].synced);
                    list.SetValue(p, i);
                }
                parametersType.GetField("parameters").SetValue(parameters, list);
                var descriptor = avatar.AddComponent(descriptorType);
                descriptorType.GetField("expressionParameters").SetValue(descriptor, parameters);
                int sdkCost = (int)parametersType.GetMethod("CalcTotalCost", Type.EmptyTypes).Invoke(parameters, null);

                var report = new AvatarStatisticsReport();
                AvatarParameterCollector.Collect(avatar, report);
                Assert.That(report.ExpressionParametersAvailable, Is.True);
                Assert.That(report.CustomParameterCount, Is.EqualTo(4));
                Assert.That(report.SyncedParameterBits, Is.EqualTo(sdkCost));
            }
            finally
            {
                Object.DestroyImmediate(avatar);
                Object.DestroyImmediate(parameters);
            }
        }

        private static Type FindType(string fullName) => AppDomain.CurrentDomain.GetAssemblies()
            .Select(a => a.GetType(fullName, false)).FirstOrDefault(t => t != null);
    }
}
