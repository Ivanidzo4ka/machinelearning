﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.ML.Runtime.Api;
using Microsoft.ML.Runtime.Data;
using Microsoft.ML.Runtime.Model;
using Microsoft.ML.Runtime.RunTests;
using Microsoft.ML.Runtime.Tools;
using Microsoft.ML.Transforms;
using System.Collections.Generic;
using System.IO;
using Xunit;
using Xunit.Abstractions;

namespace Microsoft.ML.Tests
{
    public class TensorFlowEstimatorTests : TestDataPipeBase
    {
        private class TestData
        {
            [VectorType(4)]
            public float[] a;
            [VectorType(4)]
            public float[] b;
        }
        private class TestDataSize
        {
            [VectorType(2)]
            public float[] a;
            [VectorType(2)]
            public float[] b;
        }
        private class TestDataXY
        {
            [VectorType(4)]
            public float[] A;
            [VectorType(4)]
            public float[] B;
        }
        private class TestDataDifferntType
        {
            [VectorType(4)]
            public string[] a;
            [VectorType(4)]
            public string[] b;
        }

        public TensorFlowEstimatorTests(ITestOutputHelper output) : base(output)
        {
        }

        [Fact]
        void TestSimpleCase()
        {
            var modelFile = "model_matmul/frozen_saved_model.pb";

            var dataView = ComponentCreation.CreateDataView(Env,
                new List<TestData>(new TestData[] {
                    new TestData()
                    {
                        a = new[] { 1.0f, 2.0f,3.0f, 4.0f },
                        b = new[] { 1.0f, 2.0f,3.0f, 4.0f }
                    },
                     new TestData()
                     {
                         a = new[] { 2.0f, 2.0f,2.0f, 2.0f },
                         b = new[] { 3.0f, 3.0f,3.0f, 3.0f }
                     }
                }));

            var xyData = new List<TestDataXY> { new TestDataXY() { A = new float[4], B = new float[4] } };
            var stringData = new List<TestDataDifferntType> { new TestDataDifferntType() { a = new string[4], b = new string[4] } };
            var sizeData = new List<TestDataSize> { new TestDataSize() { a = new float[2], b = new float[2] } };
            var pipe = new TensorFlowEstimator(Env, modelFile, new[] { "a", "b" }, new[] { "c" });

            var invalidDataWrongNames = ComponentCreation.CreateDataView(Env, xyData);
            var invalidDataWrongTypes = ComponentCreation.CreateDataView(Env, stringData);
            var invalidDataWrongVectorSize = ComponentCreation.CreateDataView(Env, sizeData);
            TestEstimatorCore(pipe, dataView, invalidInput: invalidDataWrongNames, validSchemaCantFitInput: invalidDataWrongVectorSize);
            TestEstimatorCore(pipe, dataView, invalidInput: invalidDataWrongTypes);
        }

        [Fact]
        void TestOldSavingAndLoading()
        {
            var modelFile = "model_matmul/frozen_saved_model.pb";

            var dataView = ComponentCreation.CreateDataView(Env,
                new List<TestData>(new TestData[] {
                    new TestData()
                    {
                        a = new[] { 1.0f, 2.0f, 3.0f, 4.0f },
                        b = new[] { 1.0f, 2.0f, 3.0f, 4.0f }
                    },
                    new TestData()
                    {
                        a = new[] { 2.0f, 2.0f, 2.0f, 2.0f },
                        b = new[] { 3.0f, 3.0f, 3.0f, 3.0f }
                    },
                    new TestData()
                    {
                        a = new[] { 5.0f, 6.0f, 10.0f, 12.0f },
                        b = new[] { 10.0f, 8.0f, 6.0f, 6.0f }
                    }
                }));
            var est = new TensorFlowEstimator(Env, modelFile, new[] { "a", "b" }, new[] { "c" });
            var transformer = est.Fit(dataView);
            var result = transformer.Transform(dataView);
            var resultRoles = new RoleMappedData(result);
            using (var ms = new MemoryStream())
            {
                TrainUtils.SaveModel(Env, Env.Start("saving"), ms, null, resultRoles);
                ms.Position = 0;
                var loadedView = ModelFileUtils.LoadTransforms(Env, dataView, ms);
                ValidateTensorFlowTransformer(loadedView);
            }
        }

        [Fact]
        void TestCommandLine()
        {
            using (var env = new TlcEnvironment())
            {
                Assert.Equal(Maml.Main(new[] { @"showschema loader=Text{col=a:R4:0-3 col=b:R4:0-3} xf=TFTransform{inputs=a inputs=b outputs=c model={model_matmul/frozen_saved_model.pb}} in=f:\2.txt" }), (int)0);
            }
        }

        private void ValidateTensorFlowTransformer(IDataView result)
        {
            result.Schema.TryGetColumnIndex("a", out int ColA);
            result.Schema.TryGetColumnIndex("b", out int ColB);
            result.Schema.TryGetColumnIndex("c", out int ColC);
            using (var cursor = result.GetRowCursor(x => true))
            {
                VBuffer<float> avalue = default;
                VBuffer<float> bvalue = default;
                VBuffer<float> cvalue = default;

                var aGetter = cursor.GetGetter<VBuffer<float>>(ColA);
                var bGetter = cursor.GetGetter<VBuffer<float>>(ColB);
                var cGetter = cursor.GetGetter<VBuffer<float>>(ColC);
                while (cursor.MoveNext())
                {
                    aGetter(ref avalue);
                    bGetter(ref bvalue);
                    cGetter(ref cvalue);
                    Assert.Equal(avalue.Values[0] * bvalue.Values[0] + avalue.Values[1] * bvalue.Values[2], cvalue.Values[0]);
                    Assert.Equal(avalue.Values[0] * bvalue.Values[1] + avalue.Values[1] * bvalue.Values[3], cvalue.Values[1]);
                    Assert.Equal(avalue.Values[2] * bvalue.Values[0] + avalue.Values[3] * bvalue.Values[2], cvalue.Values[2]);
                    Assert.Equal(avalue.Values[2] * bvalue.Values[1] + avalue.Values[3] * bvalue.Values[3], cvalue.Values[3]);
                }
            }
        }
    }
}
