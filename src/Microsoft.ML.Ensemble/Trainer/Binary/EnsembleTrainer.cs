﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.ML.Runtime;
using Microsoft.ML.Runtime.CommandLine;
using Microsoft.ML.Runtime.Data;
using Microsoft.ML.Runtime.Ensemble;
using Microsoft.ML.Runtime.Learners;
using Microsoft.ML.Runtime.Ensemble.OutputCombiners;
using Microsoft.ML.Runtime.Ensemble.Selector;
using Microsoft.ML.Runtime.Ensemble.Selector.SubModelSelector;
using Microsoft.ML.Ensemble.EntryPoints;

[assembly: LoadableClass(EnsembleTrainer.Summary, typeof(EnsembleTrainer), typeof(EnsembleTrainer.Arguments),
    new[] { typeof(SignatureBinaryClassifierTrainer), typeof(SignatureTrainer) },
    EnsembleTrainer.UserNameValue, EnsembleTrainer.LoadNameValue, "pe", "ParallelEnsemble")]

namespace Microsoft.ML.Runtime.Ensemble
{
    using TDistPredictor = IDistPredictorProducing<Single, Single>;
    using TScalarPredictor = IPredictorProducing<Single>;
    /// <summary>
    /// A generic ensemble trainer for binary classification.
    /// </summary>
    public sealed class EnsembleTrainer : EnsembleTrainerBase<Single, TScalarPredictor,
        IBinarySubModelSelector, IBinaryOutputCombiner, SignatureBinaryClassifierTrainer>,
        IModelCombiner<WeightedValue<TScalarPredictor>, TScalarPredictor>
    {
        public const string LoadNameValue = "WeightedEnsemble";
        public const string UserNameValue = "Parallel Ensemble (bagging, stacking, etc)";
        public const string Summary = "A generic ensemble classifier for binary classification.";

        public sealed class Arguments : ArgumentsBase
        {
            public Arguments()
            {
                BasePredictors = new[] { new SubComponent<ITrainer<RoleMappedData, TScalarPredictor>, SignatureBinaryClassifierTrainer>("LinearSVM") };
                OutputCombiner = new MedianFactory();
                SubModelSelectorType = new AllSelectorFactory();
            }
        }

        public EnsembleTrainer(IHostEnvironment env, Arguments args)
            : base(args, env, LoadNameValue)
        {
        }

        public override PredictionKind PredictionKind
        {
            get { return PredictionKind.BinaryClassification; }
        }

        public override TScalarPredictor CreatePredictor()
        {
            if (Models.All(m => m.Predictor is TDistPredictor))
                return new EnsembleDistributionPredictor(Host, PredictionKind, CreateModels<TDistPredictor>(), Combiner);
            return new EnsemblePredictor(Host, PredictionKind, CreateModels<TScalarPredictor>(), Combiner);
        }

        public TScalarPredictor CombineModels(IEnumerable<WeightedValue<TScalarPredictor>> models)
        {
            var weights = models.Select(m => m.Weight).ToArray();
            if (weights.All(w => w == 1))
                weights = null;
            var combiner = Args.OutputCombiner.CreateComponent(Host);
            var p = models.First().Value;

            TScalarPredictor predictor = null;
            if (p is TDistPredictor)
            {
                predictor = new EnsembleDistributionPredictor(Host, p.PredictionKind,
                    models.Select(k => new FeatureSubsetModel<TDistPredictor>((TDistPredictor)k.Value)).ToArray(),
                    combiner,
                    weights);
            }
            else
            {
                predictor = new EnsemblePredictor(Host, p.PredictionKind,
                    models.Select(k => new FeatureSubsetModel<TScalarPredictor>(k.Value)).ToArray(),
                    combiner,
                    weights);
            }

            return predictor;
        }
    }

}