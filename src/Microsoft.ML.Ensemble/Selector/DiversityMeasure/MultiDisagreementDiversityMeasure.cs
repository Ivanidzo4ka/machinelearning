﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using Microsoft.ML;
using Microsoft.ML.Data;
using Microsoft.ML.Ensemble.Selector;
using Microsoft.ML.Ensemble.Selector.DiversityMeasure;
using Microsoft.ML.Numeric;

[assembly: LoadableClass(typeof(MultiDisagreementDiversityMeasure), null, typeof(SignatureEnsembleDiversityMeasure),
    DisagreementDiversityMeasure.UserName, MultiDisagreementDiversityMeasure.LoadName)]

namespace Microsoft.ML.Ensemble.Selector.DiversityMeasure
{
    internal sealed class MultiDisagreementDiversityMeasure : BaseDisagreementDiversityMeasure<VBuffer<Single>>, IMulticlassDiversityMeasure
    {
        public const string LoadName = "MultiDisagreementDiversityMeasure";

        protected override Single GetDifference(in VBuffer<Single> valueX, in VBuffer<Single> valueY)
        {
            return (VectorUtils.ArgMax(in valueX) != VectorUtils.ArgMax(in valueY)) ? 1 : 0;
        }
    }
}
