﻿using Microsoft.ML.Runtime.Api;
using Microsoft.ML.Runtime.Data;
using Microsoft.ML.Runtime.Model;
using Microsoft.ML.Runtime.RunTests;
using Microsoft.ML.Runtime.Tools;
using System.IO;
using System.Linq;
using Xunit;
using Xunit.Abstractions;

namespace Microsoft.ML.Tests.Transformers
{
    public class KeyToBinaryVectorEstimatorTest : TestDataPipeBase
    {
        public KeyToBinaryVectorEstimatorTest(ITestOutputHelper output) : base(output)
        {
        }
        class TestClass
        {
            public int A;
            public int B;
            public int C;
        }
        class TestMeta
        {
            [VectorType(2)]
            public string[] A;
            public string B;
            [VectorType(2)]
            public int[] C;
            public int D;
            [VectorType(2)]
            public float[] E;
            public float F;
            [VectorType(2)]
            public string[] G;
            public string H;
        }

        [Fact]
        public void KeyToVectorWorkout()
        {
            var data = new[] { new TestClass() { A = 1, B = 2, C = 3, }, new TestClass() { A = 4, B = 5, C = 6 } };

            var dataView = ComponentCreation.CreateDataView(Env, data);
            dataView = new TermEstimator(Env, new[]{
                    new TermTransform.ColumnInfo("A", "TermA"),
                    new TermTransform.ColumnInfo("B", "TermB"),
                    new TermTransform.ColumnInfo("C", "TermC", textKeyValues:true)
                }).Fit(dataView).Transform(dataView);

            var pipe = new KeyToBinaryVectorEstimator(Env, new KeyToBinaryVectorTransform.ColumnInfo("TermA", "CatA"),
                new KeyToBinaryVectorTransform.ColumnInfo("TermC", "CatC"));
            TestEstimatorCore(pipe, dataView);
        }

        [Fact]
        void TestMetadataPropagation()
        {
            var data = new[] {
                new TestMeta() { A=new string[2] { "A", "B"}, B="C", C=new int[2] { 3,5}, D= 6, E= new float[2] { 1.0f,2.0f}, F = 1.0f , G= new string[2]{ "A","D"}, H="D"},
                new TestMeta() { A=new string[2] { "A", "B"}, B="C", C=new int[2] { 5,3}, D= 1, E=new float[2] { 3.0f,4.0f}, F = -1.0f ,G= new string[2]{"E", "A"}, H="E"},
                new TestMeta() { A=new string[2] { "A", "B"}, B="C", C=new int[2] { 3,5}, D= 6, E=new float[2] { 5.0f,6.0f}, F = 1.0f ,G= new string[2]{ "D", "E"}, H="D"} };


            var dataView = ComponentCreation.CreateDataView(Env, data);
            var termEst = new TermEstimator(Env,
                new TermTransform.ColumnInfo("A", "TA", textKeyValues: true),
                new TermTransform.ColumnInfo("B", "TB", textKeyValues: true),
                new TermTransform.ColumnInfo("C", "TC"),
                new TermTransform.ColumnInfo("D", "TD"));
            var termTransformer = termEst.Fit(dataView);
            dataView = termTransformer.Transform(dataView);

            var pipe = new KeyToBinaryVectorEstimator(Env,
                 new KeyToBinaryVectorTransform.ColumnInfo("TA", "CatA"),
                 new KeyToBinaryVectorTransform.ColumnInfo("TB", "CatB"),
                 new KeyToBinaryVectorTransform.ColumnInfo("TC", "CatC"),
                 new KeyToBinaryVectorTransform.ColumnInfo("TD", "CatD"));

            var result = pipe.Fit(dataView).Transform(dataView);
            ValidateMetadata(result);
        }

        void ValidateMetadata(IDataView result)
        {
            Assert.True(result.Schema.TryGetColumnIndex("CatA", out int colA));
            Assert.True(result.Schema.TryGetColumnIndex("CatB", out int colB));
            Assert.True(result.Schema.TryGetColumnIndex("CatC", out int colC));
            Assert.True(result.Schema.TryGetColumnIndex("CatD", out int colD));
            var types = result.Schema.GetMetadataTypes(colA);
            Assert.Equal(types.Select(x => x.Key), new string[1] { MetadataUtils.Kinds.SlotNames });
            VBuffer<DvText> slots = default;
            DvBool normalized = default;
            result.Schema.GetMetadata(MetadataUtils.Kinds.SlotNames, colA, ref slots);
            Assert.True(slots.Length == 6);
            Assert.Equal(slots.Values.Select(x => x.ToString()), new string[6] { "[0].Bit2", "[0].Bit1", "[0].Bit0", "[1].Bit2", "[1].Bit1", "[1].Bit0" });

            types = result.Schema.GetMetadataTypes(colB);
            Assert.Equal(types.Select(x => x.Key), new string[2] { MetadataUtils.Kinds.SlotNames, MetadataUtils.Kinds.IsNormalized });
            result.Schema.GetMetadata(MetadataUtils.Kinds.SlotNames, colB, ref slots);
            Assert.True(slots.Length == 2);
            Assert.Equal(slots.Items().Select(x => x.Value.ToString()), new string[2] { "Bit1", "Bit0" });
            result.Schema.GetMetadata(MetadataUtils.Kinds.IsNormalized, colB, ref normalized);
            Assert.True(normalized.IsTrue);

            types = result.Schema.GetMetadataTypes(colC);
            Assert.Equal(types.Select(x => x.Key), new string[0]);

            types = result.Schema.GetMetadataTypes(colD);
            Assert.Equal(types.Select(x => x.Key), new string[1] { MetadataUtils.Kinds.IsNormalized });
            result.Schema.GetMetadata(MetadataUtils.Kinds.IsNormalized, colD, ref normalized);
            Assert.True(normalized.IsTrue);
        }

        [Fact]
        void TestCommandLine()
        {
            using (var env = new TlcEnvironment())
            {
                Assert.Equal(Maml.Main(new[] { @"showschema loader=Text{col=A:R4:0} xf=Term{col=B:A} xf=KeyToBinary{col=C:B} in=f:\2.txt" }), (int)0);
            }
        }

        [Fact]
        void TestOldSavingAndLoading()
        {
            var data = new[] { new TestClass() { A = 1, B = 2, C = 3, }, new TestClass() { A = 4, B = 5, C = 6 } };
            var dataView = ComponentCreation.CreateDataView(Env, data);
            var est = new TermEstimator(Env, new[]{
                    new TermTransform.ColumnInfo("A", "TermA"),
                    new TermTransform.ColumnInfo("B", "TermB", textKeyValues:true),
                    new TermTransform.ColumnInfo("C", "TermC")
            });
            var transformer = est.Fit(dataView);
            dataView = transformer.Transform(dataView);
            var pipe = new KeyToBinaryVectorEstimator(Env,
                new KeyToBinaryVectorTransform.ColumnInfo("TermA", "CatA"),
                new KeyToBinaryVectorTransform.ColumnInfo("TermB", "CatB"),
                new KeyToBinaryVectorTransform.ColumnInfo("TermC", "CatC")
            );
            var result = pipe.Fit(dataView).Transform(dataView);
            var resultRoles = new RoleMappedData(result);
            using (var ms = new MemoryStream())
            {
                TrainUtils.SaveModel(Env, Env.Start("saving"), ms, null, resultRoles);
                ms.Position = 0;
                var loadedView = ModelFileUtils.LoadTransforms(Env, dataView, ms);
            }
        }
    }
}
