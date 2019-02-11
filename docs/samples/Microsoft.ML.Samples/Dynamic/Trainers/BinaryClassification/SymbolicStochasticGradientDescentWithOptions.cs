﻿namespace Microsoft.ML.Samples.Dynamic
{
    public sealed class SymbolicStochasticGradientDescentWithOptions
    {
        /// <summary>
        /// This example require installation of addition nuget package <a href="https://www.nuget.org/packages/Microsoft.ML.HalLearners/">Microsoft.ML.HalLearners</a>
        /// </summary>
        public static void Example()
        {
            // In this examples we will use the adult income dataset. The goal is to predict
            // if a person's income is above $50K or not, based on different pieces of information about that person.
            // For more details about this dataset, please see https://archive.ics.uci.edu/ml/datasets/adult

            // Create a new context for ML.NET operations. It can be used for exception tracking and logging, 
            // as a catalog of available operations and as the source of randomness.
            // Setting the seed to a fixed number in this examples to make outputs deterministic.
            var mlContext = new MLContext(seed: 0);

            // Download the dataset and load it as IDataView
            var data = SamplesUtils.DatasetUtils.LoadAdultDataset(mlContext);

            // Leave out 10% of data for testing
            var (trainData, testData) = mlContext.BinaryClassification.TrainTestSplit(data, testFraction: 0.1);
            // Get DefaultBinaryPipine for adult dataset and append SymSGD to it.
            var pipeline = SamplesUtils.DatasetUtils.DefaultBinaryPipeline(mlContext)
                .Append(mlContext.BinaryClassification.Trainers.SymbolicStochasticGradientDescent(
                    new Trainers.HalLearners.SymSgdClassificationTrainer.Options()
                    {
                        LabelColumn = "IsOver50K",
                        LearningRate = 0.2f,
                        NumberOfIterations = 10,
                        NumberOfThreads = 1,

                    }));
            var model = pipeline.Fit(trainData);

            // Evaluate how the model is doing on the test data
            var dataWithPredictions = model.Transform(testData);
            var metrics = mlContext.BinaryClassification.EvaluateNonCalibrated(dataWithPredictions, "IsOver50K");
            SamplesUtils.ConsoleUtils.PrintBinaryClassificationMetrics(metrics);
            // Accuracy: 0.84
            // AUC: 0.88
            // F1 Score: 0.60
            // Negative Precision: 0.87
            // Negative Recall: 0.93
            // Positive Precision: 0.69
            // Positive Recall: 0.53
        }
    }
}
