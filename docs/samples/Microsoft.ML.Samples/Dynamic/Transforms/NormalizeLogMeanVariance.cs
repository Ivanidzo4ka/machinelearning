﻿using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.ML;
using Microsoft.ML.Data;
using static Microsoft.ML.Transforms.NormalizingTransformer;

namespace Samples.Dynamic
{
    public class NormalizeLogMeanVariance
    {
        public static void Example()
        {
            // Create a new ML context, for ML.NET operations. It can be used for exception tracking and logging, 
            // as well as the source of randomness.
            var mlContext = new MLContext();
            var samples = new List<DataPoint>()
            {
                new DataPoint(){ Features = new float[4] { 1, 1, 3, 0} },
                new DataPoint(){ Features = new float[4] { 2, 2, 2, 0} },
                new DataPoint(){ Features = new float[4] { 0, 0, 1, 0} },
                new DataPoint(){ Features = new float[4] {-1,-1,-1, 1} }
            };
            // Convert training data to IDataView, the general data type used in ML.NET.
            var data = mlContext.Data.LoadFromEnumerable(samples);
            // NormalizeLogMeanVariance normalizes the data based on the computed mean and variance of the logarithm of the data.
            // Uses Cumulative distribution function as output.
            var normalize = mlContext.Transforms.NormalizeLogMeanVariance("Features", useCdf: true);

            // NormalizeLogMeanVariance normalizes the data based on the computed mean and variance of the logarithm of the data.
            var normalizeNoCdf = mlContext.Transforms.NormalizeLogMeanVariance("Features", useCdf: false);

            // Now we can transform the data and look at the output to confirm the behavior of the estimator.
            // This operation doesn't actually evaluate data until we read the data below.
            var normalizeTransform = normalize.Fit(data);
            var transformedData = normalizeTransform.Transform(data);
            var normalizeNoCdfTransform = normalizeNoCdf.Fit(data);
            var noCdfData = normalizeNoCdfTransform.Transform(data);
            var column = transformedData.GetColumn<float[]>("Features").ToArray();
            foreach (var row in column)
                Console.WriteLine(string.Join(", ", row.Select(x => x.ToString("f4"))));
            // Expected output:
            //  0.1587, 0.1587, 0.8654, 0.0000
            //  0.8413, 0.8413, 0.5837, 0.0000
            //  0.0000, 0.0000, 0.0940, 0.0000
            //  0.0000, 0.0000, 0.0000, 0.0000

            var columnFixZero = noCdfData.GetColumn<float[]>("Features").ToArray();
            foreach (var row in columnFixZero)
                Console.WriteLine(string.Join(", ", row.Select(x => x.ToString("f4"))));
            // Expected output:
            //  1.8854, 1.8854, 5.2970, 0.0000
            //  4.7708, 4.7708, 3.0925, 0.0000
            // -1.0000,-1.0000, 0.8879, 0.0000
            //  3.8854,-3.8854,-3.5213, 0.0000

            // Let's get transformation parameters. Since we work with only one column we need to pass 0 as parameter for GetNormalizerModelParameters.
            // If we have multiple column transformations we need to pass index of InputOutputColumnPair.
            var transformParams = normalizeTransform.GetNormalizerModelParameters(0) as CdfNormalizerModelParameters<ImmutableArray<float>>;
            Console.WriteLine($"Values for slot 1 would be transfromed by applying y = 0.5* (1 + ERF((Math.Log(x)- {transformParams.Mean[1]}) / ({transformParams.StandardDeviation[1]} * sqrt(2)))" );
            // ERF is https://en.wikipedia.org/wiki/Error_function.
            // Expected output:
            //  Values for slot 1 would be transfromed by applying y = 0.5* (1 + ERF((Math.Log(x)- 0.3465736) / (0.3465736 * sqrt(2)))

            var noCdfParams = normalizeNoCdfTransform.GetNormalizerModelParameters(0) as AffineNormalizerModelParameters<ImmutableArray<float>>;
            Console.WriteLine($"Values for slot 1 would be transfromed by applying y = (x - ({(noCdfParams.Offset.Length == 0 ? 0 : noCdfParams.Offset[1])})) * {noCdfParams.Scale[1]}");
            // Expected output:
            //  Values for slot 1 would be transfromed by applying y = (x - (2.88539)) * 0.3465736
        }

        private class DataPoint
        {
            [VectorType(4)]
            public float[] Features { get; set; }
        }
    }
}
