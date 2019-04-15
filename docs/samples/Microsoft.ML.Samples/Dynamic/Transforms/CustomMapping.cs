﻿using System;
using System.Collections.Generic;
using Microsoft.ML;

namespace Samples.Dynamic
{
    public static class CustomMapping
    {
        public static void Example()
        {
            // Create a new ML context, for ML.NET operations. It can be used for exception tracking and logging, 
            // as well as the source of randomness.
            var mlContext = new MLContext();

            // Get a small dataset as an IEnumerable and convert it to an IDataView.
            var samples = new List<InputData>
            {
                new InputData { Age = 26 },
                new InputData { Age = 35 },
                new InputData { Age = 34 },
                new InputData { Age = 28 },
            };
            var data = mlContext.Data.LoadFromEnumerable(samples);

            // We define the custom mapping between input and output rows that will be applied by the transformation.
            Action<InputData, CustomMappingOutput > mapping =
                (input, output) => output.IsUnderThirty = input.Age < 30;

            // Custom transformations can be used to transform data directly, or as part of a pipeline of estimators.
            // Note: If contractName is null in the CustomMapping estimator, any pipeline of estimators containing it,
            // cannot be saved and loaded back. 
            var pipeline = mlContext.Transforms.CustomMapping(mapping, contractName: null);

            // Now we can transform the data and look at the output to confirm the behavior of the estimator.
            // This operation doesn't actually evaluate data until we read the data below.
            var transformer = pipeline.Fit(data);
            var transformedData = transformer.Transform(data);

            var dataEnumerable = mlContext.Data.CreateEnumerable<TransformedData>(transformedData, reuseRowObject: true);
            Console.WriteLine("Age\t IsUnderThirty");
            foreach (var row in dataEnumerable)
                Console.WriteLine($"{row.Age}\t {row.IsUnderThirty}");

            // Expected output:
            // Age      IsUnderThirty
            // 26       True
            // 35       False
            // 34       False
            // 28       True
        }

        // Defines only the column to be generated by the custom mapping transformation in addition to the columns already present.
        private class CustomMappingOutput
        {
            public bool IsUnderThirty { get; set; }
        }

        // Defines the schema of the input data.
        private class InputData
        {
            public float Age { get; set; }
        }

        // Defines the schema of the transformed data, which includes the new column IsUnderThirty.
        private class TransformedData : InputData
        {
            public bool IsUnderThirty { get; set; }
        }
    }
}
