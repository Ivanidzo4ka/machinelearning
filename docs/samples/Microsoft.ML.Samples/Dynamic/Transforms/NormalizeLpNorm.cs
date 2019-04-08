﻿using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.ML;
using Microsoft.ML.Data;
using Microsoft.ML.Transforms;

namespace Samples.Dynamic
{
    class NormalizeLpNorm
    {
        public static void Example()
        {
            // Create a new ML context, for ML.NET operations. It can be used for exception tracking and logging, 
            // as well as the source of randomness.
            var mlContext = new MLContext();
            var samples = new List<DataPoint>()
            {
                new DataPoint(){ Features = new float[4] { 1, 1, 0, 0} },
                new DataPoint(){ Features = new float[4] { 2, 2, 0, 0} },
                new DataPoint(){ Features = new float[4] { 1, 0, 1, 0} },
                new DataPoint(){ Features = new float[4] { 0, 1, 0, 1} }
            };
            // Convert training data to IDataView, the general data type used in ML.NET.
            var data = mlContext.Data.LoadFromEnumerable(samples);
            // NormalizeLpNorm normalize rows individually by rescaling them to unit norm.
            // Performs the following operaion on a row X:  Y = (X - M) / D 
            // where M is mean vector, and D is calculated value of selected `norm` parameter .
            var approximation = mlContext.Transforms.NormalizeLpNorm("Features", norm: LpNormNormalizingEstimatorBase.NormFunction.L1, ensureZeroMean: true);

            // Now we can transform the data and look at the output to confirm the behavior of the estimator.
            // This operation doesn't actually evaluate data until we read the data below.
            var tansformer = approximation.Fit(data);
            var transformedData = tansformer.Transform(data);

            var column = transformedData.GetColumn<float[]>("Features").ToArray();
            // 0.2500,  0.2500, -0.2500, -0.2500
            // 0.2500,  0.2500, -0.2500, -0.2500
            // 0.2500, -0.2500,  0.2500, -0.2500
            //-0.2500,  0.2500, -0.2500,  0.2500
            foreach (var row in column)
                Console.WriteLine(string.Join(", ", row.Select(x => x.ToString("f4"))));
        }

        private class DataPoint
        {
            [VectorType(7)]
            public float[] Features { get; set; }
        }
    }
}
