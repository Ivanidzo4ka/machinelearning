﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.IO;
using System.Linq;
using Microsoft.Data.DataView;
using Microsoft.ML.Data;
using Microsoft.ML.Model;
using Microsoft.ML.RunTests;
using Microsoft.ML.StaticPipe;
using Microsoft.ML.Tools;
using Microsoft.ML.Transforms.Categorical;
using Xunit;
using Xunit.Abstractions;

namespace Microsoft.ML.Tests.Transformers
{
    public class CategoricalTests : TestDataPipeBase
    {
        public CategoricalTests(ITestOutputHelper output) : base(output)
        {
        }

        private sealed class TestClass
        {
            public int A;
            [VectorType(2)]
            public int[] B;
            public int[] C;

        }

        private sealed class TestMeta
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

        private sealed class TestStringClass
        {
            public string A;
        }

        [Fact]
        public void CategoricalWorkout()
        {
            var data = new[] {
                new TestClass() { A = 1, B = new int[2] { 2, 3 }, C = new int[2] { 3, 4 } },
                new TestClass() { A = 4, B = new int[2] { 2, 4 }, C = new int[3] { 2, 4, 3 } }
            };

            var dataView = ML.Data.ReadFromEnumerable(data);
            var pipe = ML.Transforms.Categorical.OneHotEncoding(new[]{
                    new OneHotEncodingEstimator.ColumnInfo("CatA", "A", OneHotEncodingTransformer.OutputKind.Bag),
                    new OneHotEncodingEstimator.ColumnInfo("CatB", "A", OneHotEncodingTransformer.OutputKind.Bin),
                    new OneHotEncodingEstimator.ColumnInfo("CatC", "A", OneHotEncodingTransformer.OutputKind.Ind),
                    new OneHotEncodingEstimator.ColumnInfo("CatD", "A", OneHotEncodingTransformer.OutputKind.Key),
                    new OneHotEncodingEstimator.ColumnInfo("CatVA", "B", OneHotEncodingTransformer.OutputKind.Bag),
                    new OneHotEncodingEstimator.ColumnInfo("CatVB", "B", OneHotEncodingTransformer.OutputKind.Bin),
                    new OneHotEncodingEstimator.ColumnInfo("CatVC", "B", OneHotEncodingTransformer.OutputKind.Ind),
                    new OneHotEncodingEstimator.ColumnInfo("CatVD", "B", OneHotEncodingTransformer.OutputKind.Key),
                    new OneHotEncodingEstimator.ColumnInfo("CatVVA", "C", OneHotEncodingTransformer.OutputKind.Bag),
                    new OneHotEncodingEstimator.ColumnInfo("CatVVB", "C", OneHotEncodingTransformer.OutputKind.Bin),
                    new OneHotEncodingEstimator.ColumnInfo("CatVVC", "C", OneHotEncodingTransformer.OutputKind.Ind),
                    new OneHotEncodingEstimator.ColumnInfo("CatVVD", "C", OneHotEncodingTransformer.OutputKind.Key),
                });

            TestEstimatorCore(pipe, dataView);
            var outputPath = GetOutputPath("Categorical", "oneHot.tsv");
            var savedData = pipe.Fit(dataView).Transform(dataView);

            using (var fs = File.Create(outputPath))
                ML.Data.SaveAsText(savedData, fs, headerRow: true, keepHidden: true);
            CheckEquality("Categorical", "oneHot.tsv");
            Done();
        }


        /// <summary>
        /// In which we take a categorical value and map it to a vector, but we get the mapping from a side data view
        /// rather than the data we are fitting.
        /// </summary>
        [Fact]
        public void CategoricalOneHotEncodingFromSideData()
        {
            // In this case, whatever the value of the input, the term mapping should come from the optional side data if specified.
            var data = new[] { new TestStringClass() { A = "Stay" }, new TestStringClass() { A = "awhile and listen" } };

            var mlContext = new MLContext();
            var dataView = mlContext.Data.ReadFromEnumerable(data);

            var sideDataBuilder = new ArrayDataViewBuilder(mlContext);
            sideDataBuilder.AddColumn("Hello", "hello", "my", "friend");
            var sideData = sideDataBuilder.GetDataView();

            var ci = new OneHotEncodingEstimator.ColumnInfo("CatA", "A", OneHotEncodingTransformer.OutputKind.Bag);
            var pipe = mlContext.Transforms.Categorical.OneHotEncoding(new[] { ci }, sideData);

            var output = pipe.Fit(dataView).Transform(dataView);

            VBuffer<ReadOnlyMemory<char>> slotNames = default;
            output.Schema["CatA"].GetSlotNames(ref slotNames);

            Assert.Equal(3, slotNames.Length);
            Assert.Equal("hello", slotNames.GetItemOrDefault(0).ToString());
            Assert.Equal("my", slotNames.GetItemOrDefault(1).ToString());
            Assert.Equal("friend", slotNames.GetItemOrDefault(2).ToString());

            Done();
        }

        [Fact]
        public void CategoricalStatic()
        {
            string dataPath = GetDataPath("breast-cancer.txt");
            var reader = TextLoaderStatic.CreateReader(ML, ctx => (
                ScalarString: ctx.LoadText(1),
                VectorString: ctx.LoadText(1, 4)));
            var data = reader.Read(dataPath);
            var wrongCollection = new[] { new TestClass() { A = 1, B = new int[2] { 2, 3 } }, new TestClass() { A = 4, B = new int[2] { 2, 4 } } };

            var invalidData = ML.Data.ReadFromEnumerable(wrongCollection);
            var est = data.MakeNewEstimator().
                  Append(row => (
                  A: row.ScalarString.OneHotEncoding(outputKind: CategoricalStaticExtensions.OneHotScalarOutputKind.Ind),
                  B: row.VectorString.OneHotEncoding(outputKind: CategoricalStaticExtensions.OneHotVectorOutputKind.Ind),
                  C: row.VectorString.OneHotEncoding(outputKind: CategoricalStaticExtensions.OneHotVectorOutputKind.Bag),
                  D: row.ScalarString.OneHotEncoding(outputKind: CategoricalStaticExtensions.OneHotScalarOutputKind.Bin),
                  E: row.VectorString.OneHotEncoding(outputKind: CategoricalStaticExtensions.OneHotVectorOutputKind.Bin)
                  ));

            TestEstimatorCore(est.AsDynamic, data.AsDynamic, invalidInput: invalidData);

            var outputPath = GetOutputPath("Categorical", "featurized.tsv");
            var savedData = ML.Data.TakeRows(est.Fit(data).Transform(data).AsDynamic, 4);
            var view = ML.Transforms.SelectColumns("A", "B", "C", "D", "E").Fit(savedData).Transform(savedData);
            using (var fs = File.Create(outputPath))
                ML.Data.SaveAsText(view, fs, headerRow: true, keepHidden: true);

            CheckEquality("Categorical", "featurized.tsv");
            Done();
        }

        [Fact]
        public void TestMetadataPropagation()
        {
            var data = new[] {
                new TestMeta() { A=new string[2] { "A", "B"}, B="C", C=new int[2] { 3,5}, D= 6, E= new float[2] { 1.0f,2.0f}, F = 1.0f , G= new string[2]{ "A","D"}, H="D"},
                new TestMeta() { A=new string[2] { "A", "B"}, B="C", C=new int[2] { 5,3}, D= 1, E=new float[2] { 3.0f,4.0f}, F = -1.0f ,G= new string[2]{"E", "A"}, H="E"},
                new TestMeta() { A=new string[2] { "A", "B"}, B="C", C=new int[2] { 3,5}, D= 6, E=new float[2] { 5.0f,6.0f}, F = 1.0f ,G= new string[2]{ "D", "E"}, H="D"} };


            var dataView = ML.Data.ReadFromEnumerable(data);
            var pipe = ML.Transforms.Categorical.OneHotEncoding(new[] {
                new OneHotEncodingEstimator.ColumnInfo("CatA", "A", OneHotEncodingTransformer.OutputKind.Bag),
                new OneHotEncodingEstimator.ColumnInfo("CatB", "B", OneHotEncodingTransformer.OutputKind.Bag),
                new OneHotEncodingEstimator.ColumnInfo("CatC", "C", OneHotEncodingTransformer.OutputKind.Bag),
                new OneHotEncodingEstimator.ColumnInfo("CatD", "D", OneHotEncodingTransformer.OutputKind.Bag),
                new OneHotEncodingEstimator.ColumnInfo("CatE", "E",OneHotEncodingTransformer.OutputKind.Ind),
                new OneHotEncodingEstimator.ColumnInfo("CatF", "F", OneHotEncodingTransformer.OutputKind.Ind),
                new OneHotEncodingEstimator.ColumnInfo("CatG", "G", OneHotEncodingTransformer.OutputKind.Key),
                new OneHotEncodingEstimator.ColumnInfo("CatH", "H", OneHotEncodingTransformer.OutputKind.Key),
                new OneHotEncodingEstimator.ColumnInfo("CatI", "A", OneHotEncodingTransformer.OutputKind.Bin),
                new OneHotEncodingEstimator.ColumnInfo("CatJ", "B", OneHotEncodingTransformer.OutputKind.Bin),
                new OneHotEncodingEstimator.ColumnInfo("CatK", "C", OneHotEncodingTransformer.OutputKind.Bin),
                new OneHotEncodingEstimator.ColumnInfo("CatL", "D", OneHotEncodingTransformer.OutputKind.Bin) });


            var result = pipe.Fit(dataView).Transform(dataView);

            ValidateMetadata(result);
            Done();
        }


        private void ValidateMetadata(IDataView result)
        {
            VBuffer<ReadOnlyMemory<char>> slots = default;
            VBuffer<int> slotRanges = default;

            var column = result.Schema["CatA"];
            Assert.Equal(column.Metadata.Schema.Select(x => x.Name), new string[1] { MetadataUtils.Kinds.SlotNames });
            column.GetSlotNames(ref slots);
            Assert.True(slots.Length == 2);
            Assert.Equal(slots.Items().Select(x => x.Value.ToString()), new string[2] { "A", "B" });

            column = result.Schema["CatB"];
            Assert.Equal(column.Metadata.Schema.Select(x => x.Name), new string[2] { MetadataUtils.Kinds.SlotNames, MetadataUtils.Kinds.IsNormalized });
            column.GetSlotNames(ref slots);
            Assert.True(slots.Length == 1);
            Assert.Equal(slots.Items().Select(x => x.Value.ToString()), new string[1] { "C" });
            Assert.True(column.IsNormalized());

            column = result.Schema["CatC"];
            Assert.Equal(column.Metadata.Schema.Select(x => x.Name), new string[1] { MetadataUtils.Kinds.SlotNames });
            column.GetSlotNames(ref slots);
            Assert.True(slots.Length == 2);
            Assert.Equal(slots.Items().Select(x => x.Value.ToString()), new string[2] { "3", "5" });

            column = result.Schema["CatD"];
            Assert.Equal(column.Metadata.Schema.Select(x => x.Name), new string[2] { MetadataUtils.Kinds.SlotNames, MetadataUtils.Kinds.IsNormalized });
            column.GetSlotNames(ref slots);
            Assert.True(slots.Length == 2);
            Assert.Equal(slots.Items().Select(x => x.Value.ToString()), new string[2] { "6", "1" });
            Assert.True(column.IsNormalized());

            column = result.Schema["CatE"];
            Assert.Equal(column.Metadata.Schema.Select(x => x.Name), new string[3] { MetadataUtils.Kinds.SlotNames, MetadataUtils.Kinds.CategoricalSlotRanges, MetadataUtils.Kinds.IsNormalized });
            column.GetSlotNames(ref slots);
            Assert.True(slots.Length == 12);
            Assert.Equal(slots.Items().Select(x => x.Value.ToString()), new string[12] { "[0].1", "[0].2", "[0].3", "[0].4", "[0].5", "[0].6", "[1].1", "[1].2", "[1].3", "[1].4", "[1].5", "[1].6" });
            column.Metadata.GetValue(MetadataUtils.Kinds.CategoricalSlotRanges, ref slotRanges);
            Assert.True(slotRanges.Length == 4);
            Assert.Equal(slotRanges.Items().Select(x => x.Value.ToString()), new string[4] { "0", "5", "6", "11" });
            Assert.True(column.IsNormalized());

            column = result.Schema["CatF"];
            Assert.Equal(column.Metadata.Schema.Select(x => x.Name), new string[3] { MetadataUtils.Kinds.SlotNames, MetadataUtils.Kinds.CategoricalSlotRanges, MetadataUtils.Kinds.IsNormalized });
            column.GetSlotNames(ref slots);
            Assert.True(slots.Length == 2);
            Assert.Equal(slots.Items().Select(x => x.Value.ToString()), new string[2] { "1", "-1" });
            column.Metadata.GetValue(MetadataUtils.Kinds.CategoricalSlotRanges, ref slotRanges);
            Assert.True(slotRanges.Length == 2);
            Assert.Equal(slotRanges.Items().Select(x => x.Value.ToString()), new string[2] { "0", "1" });
            Assert.True(column.IsNormalized());

            column = result.Schema["CatG"];
            Assert.Equal(column.Metadata.Schema.Select(x => x.Name), new string[1] { MetadataUtils.Kinds.KeyValues });
            column.GetKeyValues(ref slots);
            Assert.True(slots.Length == 3);
            Assert.Equal(slots.Items().Select(x => x.Value.ToString()), new string[3] { "A", "D", "E" });

            column = result.Schema["CatH"];
            Assert.Equal(column.Metadata.Schema.Select(x => x.Name), new string[1] { MetadataUtils.Kinds.KeyValues });
            column.GetKeyValues(ref slots);
            Assert.True(slots.Length == 2);
            Assert.Equal(slots.Items().Select(x => x.Value.ToString()), new string[2] { "D", "E" });

            column = result.Schema["CatI"];
            Assert.Equal(column.Metadata.Schema.Select(x => x.Name), new string[1] { MetadataUtils.Kinds.SlotNames });
            column.GetSlotNames(ref slots);
            Assert.True(slots.Length == 6);
            Assert.Equal(slots.Items().Select(x => x.Value.ToString()), new string[6] { "[0].Bit2", "[0].Bit1", "[0].Bit0", "[1].Bit2", "[1].Bit1", "[1].Bit0" });

            column = result.Schema["CatJ"];
            Assert.Equal(column.Metadata.Schema.Select(x => x.Name), new string[2] { MetadataUtils.Kinds.SlotNames, MetadataUtils.Kinds.IsNormalized });
            column.GetSlotNames(ref slots);
            Assert.True(slots.Length == 2);
            Assert.Equal(slots.Items().Select(x => x.Value.ToString()), new string[2] { "Bit1", "Bit0" });
            Assert.True(column.IsNormalized());

            column = result.Schema["CatK"];
            Assert.Equal(column.Metadata.Schema.Select(x => x.Name), new string[1] { MetadataUtils.Kinds.SlotNames });
            column.GetSlotNames(ref slots);
            Assert.True(slots.Length == 6);
            Assert.Equal(slots.Items().Select(x => x.Value.ToString()), new string[6] { "[0].Bit2", "[0].Bit1", "[0].Bit0", "[1].Bit2", "[1].Bit1", "[1].Bit0" });

            column = result.Schema["CatL"];
            Assert.Equal(column.Metadata.Schema.Select(x => x.Name), new string[2] { MetadataUtils.Kinds.SlotNames, MetadataUtils.Kinds.IsNormalized });
            column.GetSlotNames(ref slots);
            Assert.True(slots.Length == 3);
            Assert.Equal(slots.Items().Select(x => x.Value.ToString()), new string[3] { "Bit2", "Bit1", "Bit0" });
            Assert.True(column.IsNormalized());
        }

        [Fact]
        public void TestCommandLine()
        {
            Assert.Equal(0, Maml.Main(new[] { @"showschema loader=Text{col=A:R4:0} xf=Cat{col=B:A} in=f:\2.txt" }));
        }

        [Fact]
        public void TestOldSavingAndLoading()
        {
            var data = new[] {
                new TestClass() { A = 1, B = new int[2] { 2, 3 }, C = new int[2] { 3, 4 } },
                new TestClass() { A = 4, B = new int[2] { 2, 4 }, C = new int[3] { 2, 4, 3 } }
            };
            var dataView = ML.Data.ReadFromEnumerable(data);
            var pipe = ML.Transforms.Categorical.OneHotEncoding(new[]{
                    new OneHotEncodingEstimator.ColumnInfo("CatA", "A"),
                    new OneHotEncodingEstimator.ColumnInfo("CatB", "B"),
                    new OneHotEncodingEstimator.ColumnInfo("CatC", "C")
            });
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
