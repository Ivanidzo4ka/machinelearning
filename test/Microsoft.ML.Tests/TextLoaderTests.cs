// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.ML;
using Microsoft.ML.Data;
using Microsoft.ML.Runtime;
using Microsoft.ML.Runtime.Api;
using Microsoft.ML.Runtime.Data;
using Microsoft.ML.Runtime.ImageAnalytics;
using Microsoft.ML.Runtime.Model;
using Microsoft.ML.TestFramework;
using System;
using System.IO;
using Xunit;
using Xunit.Abstractions;

namespace Microsoft.ML.EntryPoints.Tests
{
    public class TextLoaderTests : BaseTestClass
    {
        public TextLoaderTests(ITestOutputHelper output)
            : base(output)
        {

        }

        [Fact]
        public void ConstructorDoesntThrow()
        {
            Assert.NotNull(new Data.TextLoader("fakeFile.txt").CreateFrom<Input>());
            Assert.NotNull(new Data.TextLoader("fakeFile.txt").CreateFrom<Input>(useHeader: true));
            Assert.NotNull(new Data.TextLoader("fakeFile.txt").CreateFrom<Input>());
            Assert.NotNull(new Data.TextLoader("fakeFile.txt").CreateFrom<Input>(useHeader: false));
            Assert.NotNull(new Data.TextLoader("fakeFile.txt").CreateFrom<Input>(useHeader: false, supportSparse: false, trimWhitespace: false));
            Assert.NotNull(new Data.TextLoader("fakeFile.txt").CreateFrom<Input>(useHeader: false, supportSparse: false));
            Assert.NotNull(new Data.TextLoader("fakeFile.txt").CreateFrom<Input>(useHeader: false, allowQuotedStrings: false));

            Assert.NotNull(new Data.TextLoader("fakeFile.txt").CreateFrom<InputWithUnderscore>());
        }


        [Fact]
        public void CanSuccessfullyApplyATransform()
        {
            var loader = new Data.TextLoader("fakeFile.txt").CreateFrom<Input>();

            using (var environment = new TlcEnvironment())
            {
                Experiment experiment = environment.CreateExperiment();
                ILearningPipelineDataStep output = loader.ApplyStep(null, experiment) as ILearningPipelineDataStep;

                Assert.NotNull(output.Data);
                Assert.NotNull(output.Data.VarName);
                Assert.Null(output.Model);
            }
        }

        [Fact]
        public void CanSuccessfullyRetrieveQuotedData()
        {
            string dataPath = GetDataPath("QuotingData.csv");
            var loader = new Data.TextLoader(dataPath).CreateFrom<QuoteInput>(useHeader: true, separator: ',', allowQuotedStrings: true, supportSparse: false);

            using (var environment = new TlcEnvironment())
            {
                Experiment experiment = environment.CreateExperiment();
                ILearningPipelineDataStep output = loader.ApplyStep(null, experiment) as ILearningPipelineDataStep;

                experiment.Compile();
                loader.SetInput(environment, experiment);
                experiment.Run();

                IDataView data = experiment.GetOutput(output.Data);
                Assert.NotNull(data);

                using (var cursor = data.GetRowCursor((a => true)))
                {
                    var IDGetter = cursor.GetGetter<float>(0);
                    var TextGetter = cursor.GetGetter<DvText>(1);

                    Assert.True(cursor.MoveNext());

                    float ID = 0;
                    IDGetter(ref ID);
                    Assert.Equal(1, ID);

                    DvText Text = new DvText();
                    TextGetter(ref Text);
                    Assert.Equal("This text contains comma, within quotes.", Text.ToString());

                    Assert.True(cursor.MoveNext());

                    ID = 0;
                    IDGetter(ref ID);
                    Assert.Equal(2, ID);

                    Text = new DvText();
                    TextGetter(ref Text);
                    Assert.Equal("This text contains extra punctuations and special characters.;*<>?!@#$%^&*()_+=-{}|[]:;'", Text.ToString());

                    Assert.True(cursor.MoveNext());

                    ID = 0;
                    IDGetter(ref ID);
                    Assert.Equal(3, ID);

                    Text = new DvText();
                    TextGetter(ref Text);
                    Assert.Equal("This text has no quotes", Text.ToString());

                    Assert.False(cursor.MoveNext());
                }
            }
        }

        [Fact]
        public void CanSuccessfullyRetrieveSparseData()
        {
            string dataPath = GetDataPath("SparseData.txt");
            var loader = new Data.TextLoader(dataPath).CreateFrom<SparseInput>(useHeader: true, allowQuotedStrings: false, supportSparse: true);

            using (var environment = new TlcEnvironment())
            {
                Experiment experiment = environment.CreateExperiment();
                ILearningPipelineDataStep output = loader.ApplyStep(null, experiment) as ILearningPipelineDataStep;

                experiment.Compile();
                loader.SetInput(environment, experiment);
                experiment.Run();

                IDataView data = experiment.GetOutput(output.Data);
                Assert.NotNull(data);

                using (var cursor = data.GetRowCursor((a => true)))
                {
                    var getters = new ValueGetter<float>[]{
                        cursor.GetGetter<float>(0),
                        cursor.GetGetter<float>(1),
                        cursor.GetGetter<float>(2),
                        cursor.GetGetter<float>(3),
                        cursor.GetGetter<float>(4)
                    };


                    Assert.True(cursor.MoveNext());

                    float[] targets = new float[] { 1, 2, 3, 4, 5 };
                    for (int i = 0; i < getters.Length; i++)
                    {
                        float value = 0;
                        getters[i](ref value);
                        Assert.Equal(targets[i], value);
                    }

                    Assert.True(cursor.MoveNext());

                    targets = new float[] { 0, 0, 0, 4, 5 };
                    for (int i = 0; i < getters.Length; i++)
                    {
                        float value = 0;
                        getters[i](ref value);
                        Assert.Equal(targets[i], value);
                    }

                    Assert.True(cursor.MoveNext());

                    targets = new float[] { 0, 2, 0, 0, 0 };
                    for (int i = 0; i < getters.Length; i++)
                    {
                        float value = 0;
                        getters[i](ref value);
                        Assert.Equal(targets[i], value);
                    }

                    Assert.False(cursor.MoveNext());
                }
            }

        }

        [Fact]
        public void CanSuccessfullyTrimSpaces()
        {
            string dataPath = GetDataPath("TrimData.csv");
            var loader = new Data.TextLoader(dataPath).CreateFrom<QuoteInput>(useHeader: true, separator: ',', allowQuotedStrings: false, supportSparse: false, trimWhitespace: true);

            using (var environment = new TlcEnvironment())
            {
                Experiment experiment = environment.CreateExperiment();
                ILearningPipelineDataStep output = loader.ApplyStep(null, experiment) as ILearningPipelineDataStep;

                experiment.Compile();
                loader.SetInput(environment, experiment);
                experiment.Run();

                IDataView data = experiment.GetOutput(output.Data);
                Assert.NotNull(data);

                using (var cursor = data.GetRowCursor((a => true)))
                {
                    var IDGetter = cursor.GetGetter<float>(0);
                    var TextGetter = cursor.GetGetter<DvText>(1);

                    Assert.True(cursor.MoveNext());

                    float ID = 0;
                    IDGetter(ref ID);
                    Assert.Equal(1, ID);

                    DvText Text = new DvText();
                    TextGetter(ref Text);
                    Assert.Equal("There is a space at the end", Text.ToString());

                    Assert.True(cursor.MoveNext());

                    ID = 0;
                    IDGetter(ref ID);
                    Assert.Equal(2, ID);

                    Text = new DvText();
                    TextGetter(ref Text);
                    Assert.Equal("There is no space at the end", Text.ToString());

                    Assert.False(cursor.MoveNext());
                }
            }
        }

        [Fact]
        public void ThrowsExceptionWithPropertyName()
        {
            Exception ex = Assert.Throws<InvalidOperationException>(() => new Data.TextLoader("fakefile.txt").CreateFrom<ModelWithoutColumnAttribute>());
            Assert.StartsWith("String1 is missing ColumnAttribute", ex.Message);
        }

        public class QuoteInput
        {
            [Column("0")]
            public float ID;

            [Column("1")]
            public string Text;
        }

        public class SparseInput
        {
            [Column("0")]
            public float C1;

            [Column("1")]
            public float C2;

            [Column("2")]
            public float C3;

            [Column("3")]
            public float C4;

            [Column("4")]
            public float C5;
        }

        public class Input
        {
            [Column("0")]
            public string String1;

            [Column("1")]
            public float Number1;
        }

        public class InputWithUnderscore
        {
            [Column("0")]
            public string String_1;

            [Column("1")]
            public float Number_1;
        }

        public class ModelWithoutColumnAttribute
        {
            public string String1;
        }

        [Fact]
        public void TestImages_Bitmap()
        {
            using (var env = new TlcEnvironment(conc:1))
            {
                var data = env.CreateLoader("Text{col=ImagePath:TX:0}", new MultiFileSource(@"D:\images.csv"));
                var images = new ImageLoader_BitmapTransform(env, new ImageLoader_BitmapTransform.Arguments()
                {
                    Column = new ImageLoader_BitmapTransform.Column[1]
                    {
                        new ImageLoader_BitmapTransform.Column() { Source=  "ImagePath", Name="ImageReal" }
                    }
                }, data);
                var cropped = new ImageResizer_BitmapTransform(env, new ImageResizer_BitmapTransform.Arguments()
                {
                    Column = new ImageResizer_BitmapTransform.Column[1]{
                        new ImageResizer_BitmapTransform.Column() {  Name= "ImageCropped", Source = "ImageReal", ImageHeight =100, ImageWidth = 100},
                    },
                    ResizeFunction = new ResizeWithPadding.Arguments() {}

                }, images);

                var pixels = new ImagePixelExtractor_BitmapTransform(env, new ImagePixelExtractor_BitmapTransform.Arguments()
                {
                    Column = new ImagePixelExtractor_BitmapTransform.Column[1]{
                        new ImagePixelExtractor_BitmapTransform.Column() {  Source= "ImageCropped", Name = "ImagePixels"}
                    }
                }, cropped);
                
                using (var memoryStream = File.OpenWrite(@"D:\tlc\image_model.zip"))
                {
                    using (var ch = env.Start("Saving transform model"))
                    {
                        using (var rep = RepositoryWriter.CreateNew(memoryStream, ch))
                        {
                            ch.Trace("Saving root schema and transformations");
                            TrainUtils.SaveDataPipe(env, rep, pixels);
                            rep.Commit();
                        }
                        ch.Done();
                    }
                }

                
                pixels.Schema.TryGetColumnIndex("ImagePixels", out int cropColumn);
                using (var cursor = pixels.GetRowCursor((x) => x == cropColumn))
                {
                    var pixelsGetter = cursor.GetGetter<VBuffer<float>>(cropColumn);
                    VBuffer<float> pixelcolumn = new VBuffer<float>();
                    while (cursor.MoveNext())
                    {
                        pixelsGetter(ref pixelcolumn);
                    }
                }

            }
        }

        [Fact]
        public void TestImages()
        {
            using (var env = new TlcEnvironment())
            {
                var data = env.CreateLoader("Text{col=ImagePath:TX:0}", new MultiFileSource(@"D:\images.csv"));
                var images = new ImageLoaderTransform(env, new ImageLoaderTransform.Arguments()
                {
                    Column = new ImageLoaderTransform.Column[1]
                    {
                        new ImageLoaderTransform.Column() { Source=  "ImagePath", Name="ImageReal" }
                    }
                }, data);
                var cropped = new ImageResizerTransform(env, new ImageResizerTransform.Arguments()
                {
                    Column = new ImageResizerTransform.Column[1]{
                        new ImageResizerTransform.Column() {  Name= "ImageCropped", Source = "ImageReal", ImageHeight =100, ImageWidth = 100, Resizing = ImageResizerTransform.ResizingKind.IsoPad}
                    }
                }, images);

                var pixels = new ImagePixelExtractorTransform(env, new ImagePixelExtractorTransform.Arguments()
                {
                    Column = new ImagePixelExtractorTransform.Column[1]{
                        new ImagePixelExtractorTransform.Column() {  Source= "ImageCropped", Name = "ImagePixels"}
                    }
                }, cropped);


                pixels.Schema.TryGetColumnIndex("ImagePixels", out int cropColumn);
                using (var cursor = pixels.GetRowCursor((x) => x == cropColumn))
                {
                    var pixelsGetter = cursor.GetGetter<VBuffer<float>>(cropColumn);
                    VBuffer<float> pixelcolumn = new VBuffer<float>();
                    while (cursor.MoveNext())
                    {
                        pixelsGetter(ref pixelcolumn);
                    }
                }

            }

        }
    }
}
