﻿using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Bot.Builder.Dialogs;

namespace Microsoft.Bot.Builder.Planning.Rules
{
    public class DoStepsRule : UtteranceRecognizeRule
    {
        public DoStepsRule(string intent = null, List<string> entities = null, List<IDialog> steps = null)
            : base(intent, entities, steps)
        {
        }
    }
}
