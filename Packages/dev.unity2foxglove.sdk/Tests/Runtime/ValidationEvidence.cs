// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Classifies what validation output proves, independently of where it runs.

using System;
using System.IO;
using System.Text;

namespace Unity.FoxgloveSDK.Tests
{
    [Flags]
    internal enum ValidationEvidence
    {
        None = 0,
        Behavior = 1 << 0,
        Structural = 1 << 1,
        Conformance = 1 << 2,
        FaultInjection = 1 << 3,
        Performance = 1 << 4,
        ManualEvidence = 1 << 5
    }

    internal static class ValidationEvidenceFormatter
    {
        internal const ValidationEvidence All =
            ValidationEvidence.Behavior
            | ValidationEvidence.Structural
            | ValidationEvidence.Conformance
            | ValidationEvidence.FaultInjection
            | ValidationEvidence.Performance
            | ValidationEvidence.ManualEvidence;

        private static readonly (ValidationEvidence Evidence, string Label)[] OrderedLabels =
        {
            (ValidationEvidence.Behavior, "[BEHAVIOR]"),
            (ValidationEvidence.Structural, "[STRUCTURAL]"),
            (ValidationEvidence.Conformance, "[CONFORMANCE]"),
            (ValidationEvidence.FaultInjection, "[FAULT_INJECTION]"),
            (ValidationEvidence.Performance, "[PERFORMANCE]"),
            (ValidationEvidence.ManualEvidence, "[MANUAL_EVIDENCE]")
        };

        public static string Format(ValidationEvidence evidence)
        {
            Validate(evidence);

            var builder = new StringBuilder();
            foreach (var item in OrderedLabels)
            {
                if ((evidence & item.Evidence) == 0)
                    continue;

                if (builder.Length > 0)
                    builder.Append(' ');
                builder.Append(item.Label);
            }

            return builder.ToString();
        }

        public static void Validate(ValidationEvidence evidence)
        {
            if (evidence == ValidationEvidence.None || (evidence & ~All) != 0)
                throw new ArgumentOutOfRangeException(nameof(evidence), evidence, "Validation evidence must contain only known non-zero values.");
        }
    }

    internal sealed class ValidationEvidenceTextWriter : TextWriter
    {
        private readonly TextWriter _inner;
        private readonly string _prefix;
        private readonly object _sync = new();
        private bool _atLineStart = true;

        public ValidationEvidenceTextWriter(TextWriter inner, ValidationEvidence evidence)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
            _prefix = ValidationEvidenceFormatter.Format(evidence) + " ";
        }

        public override Encoding Encoding => _inner.Encoding;

        public override IFormatProvider FormatProvider => _inner.FormatProvider;

        public override string NewLine
        {
            get => _inner.NewLine;
            set => _inner.NewLine = value;
        }

        public override void Write(char value)
        {
            lock (_sync)
                WriteCore(value);
        }

        public override void Write(string value)
        {
            if (value == null)
                return;

            lock (_sync)
            {
                for (var i = 0; i < value.Length; i++)
                    WriteCore(value[i]);
            }
        }

        public override void Write(char[] buffer, int index, int count)
        {
            if (buffer == null)
                throw new ArgumentNullException(nameof(buffer));
            if (index < 0 || count < 0 || buffer.Length - index < count)
                throw new ArgumentOutOfRangeException();

            lock (_sync)
            {
                for (var i = index; i < index + count; i++)
                    WriteCore(buffer[i]);
            }
        }

        public override void Flush()
        {
            lock (_sync)
                _inner.Flush();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                Flush();
            base.Dispose(disposing);
        }

        private void WriteCore(char value)
        {
            if (_atLineStart && value != '\r' && value != '\n')
            {
                _inner.Write(_prefix);
                _atLineStart = false;
            }

            _inner.Write(value);
            if (value == '\r' || value == '\n')
                _atLineStart = true;
        }
    }
}
