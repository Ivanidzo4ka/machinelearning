﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.ML.Runtime.Api;
using Microsoft.ML.Runtime.Data;
using Microsoft.ML.Runtime.Model;
using Microsoft.ML.Runtime.RunTests;
using Microsoft.ML.Runtime.Tools;
using System;
using System.IO;
using Xunit;
using Xunit.Abstractions;

namespace Microsoft.ML.Tests
{
    public class TermEstimatorTests : TestDataPipeBase
    {
        public TermEstimatorTests(ITestOutputHelper output) : base(output)
        {
        }

        class TestClass
        {
            public int A;
            public int B;
            public int C;
        }

        class TestClassXY
        {
            public int X;
            public int Y;
        }

        class TestClassDifferentTypes
        {
            public string A;
            public string B;
            public string C;
        }


        class TestMetaClass
        {
            public int NotUsed;
            public string Term;
        }

        [Fact]
        void TestWorking()
        {
            var data = new[] { new TestClass() { A = 1, B = 2, C = 3, }, new TestClass() { A = 4, B = 5, C = 6 } };
            var xydata = new[] { new TestClassXY() { X = 10, Y = 100 }, new TestClassXY() { X = -1, Y = -100 } };
            var stringData = new[] { new TestClassDifferentTypes { A = "1", B = "c", C = "b" } };
            using (var env = new TlcEnvironment())
            {
                var dataView = ComponentCreation.CreateDataView(env, data);
                var pipe = new TermEstimator(env, new[]{
                    new TermTransform.ColumnInfo("A", "TermA"),
                    new TermTransform.ColumnInfo("B", "TermB"),
                    new TermTransform.ColumnInfo("C", "TermC")
                });
                var invalidData = ComponentCreation.CreateDataView(env, xydata);
                TestEstimatorCore(pipe, dataView, null, invalidData);
            }
        }

        [Fact]
        void TestBadTransformSchema()
        {
            var data = new[] { new TestClass() { A = 1, B = 2, C = 3, }, new TestClass() { A = 4, B = 5, C = 6 } };
            var xydata = new[] { new TestClassXY() { X = 10, Y = 100 }, new TestClassXY() { X = -1, Y = -100 } };
            var stringData = new[] { new TestClassDifferentTypes { A = "1", B = "c", C = "b" } };
            using (var env = new TlcEnvironment())
            {
                var dataView = ComponentCreation.CreateDataView(env, data);
                var xyDataView = ComponentCreation.CreateDataView(env, xydata);
                var est = new TermEstimator(env, new[]{
                    new TermTransform.ColumnInfo("A", "TermA"),
                    new TermTransform.ColumnInfo("B", "TermB"),
                    new TermTransform.ColumnInfo("C", "TermC")
                });
                var transformer = est.Fit(dataView);
                var stringView = ComponentCreation.CreateDataView(env, stringData);
                try
                {
                    var result = transformer.Transform(stringView);
                    Assert.False(true);
                }
                catch(InvalidOperationException)
                {
                }
            }
        }

        [Fact]
        void TestSavingAndLoading()
        {
            var data = new[] { new TestClass() { A = 1, B = 2, C = 3, }, new TestClass() { A = 4, B = 5, C = 6 } };
            using (var env = new TlcEnvironment())
            {
                var dataView = ComponentCreation.CreateDataView(env, data);
                var est = new TermEstimator(env, new[]{
                    new TermTransform.ColumnInfo("A", "TermA"),
                    new TermTransform.ColumnInfo("B", "TermB"),
                    new TermTransform.ColumnInfo("C", "TermC")
                });
                var transformer = est.Fit(dataView);
                using (var ms = new MemoryStream())
                {
                    transformer.SaveTo(env, ms);
                    ms.Position = 0;
                    var loadedTransformer = TransformerChain.LoadFrom(env, ms);
                    var result = loadedTransformer.Transform(dataView);
                    ValidateTermTransformer(result);
                }
            }
        }

        [Fact]
        void TestOldSavingAndLoading()
        {
            var data = new[] { new TestClass() { A = 1, B = 2, C = 3, }, new TestClass() { A = 4, B = 5, C = 6 } };
            using (var env = new TlcEnvironment())
            {
                var dataView = ComponentCreation.CreateDataView(env, data);
                var est = new TermEstimator(env, new[]{
                    new TermTransform.ColumnInfo("A", "TermA"),
                    new TermTransform.ColumnInfo("B", "TermB"),
                    new TermTransform.ColumnInfo("C", "TermC")
                });
                var transformer = est.Fit(dataView);
                var result = transformer.Transform(dataView);
                var resultRoles = new RoleMappedData(result);
                using (var ms = new MemoryStream())
                {
                    TrainUtils.SaveModel(env, env.Start("saving"), ms, null, resultRoles);
                    ms.Position = 0;
                    var loadedView = ModelFileUtils.LoadTransforms(env, dataView, ms);
                    ValidateTermTransformer(loadedView);
                }
            }
        }

        [Fact]
        void TestMetadataCopy()
        {
            var data = new[] { new TestMetaClass() { Term = "A", NotUsed = 1 }, new TestMetaClass() { Term = "B" }, new TestMetaClass() { Term = "C" } };
            using (var env = new TlcEnvironment())
            {
                var dataView = ComponentCreation.CreateDataView(env, data);
                var termEst = new TermEstimator(env, new[] {
                    new TermTransform.ColumnInfo("Term" ,"T") });
                var termTransformer = termEst.Fit(dataView);
                var result = termTransformer.Transform(dataView);

                result.Schema.TryGetColumnIndex("T", out int termIndex);
                var names1 = default(VBuffer<DvText>);
                var type1 = result.Schema.GetColumnType(termIndex);
                int size = type1.ItemType.IsKey ? type1.ItemType.KeyCount : -1;
                result.Schema.GetMetadata(MetadataUtils.Kinds.KeyValues, termIndex, ref names1);
                Assert.True(names1.Count > 0);
            }
        }

        [Fact]
        void TestCommandLine()
        {
            using (var env = new TlcEnvironment())
            {
                Assert.Equal(Maml.Main(new[] { @"showschema loader=Text{col=A:R4:0} xf=Term{col=B:A} in=f:\2.txt" }), (int)0);
            }
        }

        private void ValidateTermTransformer(IDataView result)
        {
            result.Schema.TryGetColumnIndex("TermA", out int ColA);
            result.Schema.TryGetColumnIndex("TermB", out int ColB);
            result.Schema.TryGetColumnIndex("TermC", out int ColC);
            using (var cursor = result.GetRowCursor(x => true))
            {
                uint avalue = 0;
                uint bvalue = 0;
                uint cvalue = 0;

                var aGetter = cursor.GetGetter<uint>(ColA);
                var bGetter = cursor.GetGetter<uint>(ColB);
                var cGetter = cursor.GetGetter<uint>(ColC);
                uint i = 1;
                while (cursor.MoveNext())
                {
                    aGetter(ref avalue);
                    bGetter(ref bvalue);
                    cGetter(ref cvalue);
                    Assert.Equal(i, avalue);
                    Assert.Equal(i, bvalue);
                    Assert.Equal(i, cvalue);
                    i++;
                }
            }
        }
    }
}

