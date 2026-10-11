// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Unity2Foxglove.Ros2Bridge/Protocol
// Purpose: Strict, canonical U2R2 envelope and v2 header codec.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Unity2Foxglove.Ros2Bridge.Protocol
{
    public static partial class U2R2ProtocolCodec
    {
        private sealed class StableErrorRule
        {
            public StableErrorRule(
                bool terminal,
                params U2R2Operation[] responseOperations)
            {
                Terminal = terminal;
                ResponseOperations = new HashSet<U2R2Operation>(
                    responseOperations ?? Array.Empty<U2R2Operation>());
            }

            public bool Terminal { get; }

            public HashSet<U2R2Operation> ResponseOperations { get; }
        }

        private static class Rfc8259JsonValidator
        {
            public static void Validate(
                string json,
                ulong maxJsonDepth)
            {
                if (json == null)
                    throw InvalidFrame("The U2R2 JSON header is invalid.");

                var parser = new Parser(json, maxJsonDepth);
                parser.SkipWhitespace();
                parser.ParseValue(depth: 0);
                parser.SkipWhitespace();
                if (!parser.AtEnd)
                    throw InvalidFrame("The U2R2 JSON header has trailing content.");
            }

            private sealed class Parser
            {
                private readonly string _json;
                private readonly ulong _maxJsonDepth;
                private int _index;

                public Parser(string json, ulong maxJsonDepth)
                {
                    _json = json;
                    _maxJsonDepth = maxJsonDepth;
                }

                public bool AtEnd => _index == _json.Length;

                public void SkipWhitespace()
                {
                    while (_index < _json.Length)
                    {
                        switch (_json[_index])
                        {
                            case ' ':
                            case '\t':
                            case '\r':
                            case '\n':
                                _index++;
                                continue;
                            default:
                                return;
                        }
                    }
                }

                public void ParseValue(int depth)
                {
                    if (depth < 0
                        || checked((ulong)depth) > _maxJsonDepth
                        || _index >= _json.Length)
                        Fail();

                    switch (_json[_index])
                    {
                        case '{':
                            ParseObject(depth);
                            return;
                        case '[':
                            ParseArray(depth);
                            return;
                        case '"':
                            ParseString();
                            return;
                        case 't':
                            ParseLiteral("true");
                            return;
                        case 'f':
                            ParseLiteral("false");
                            return;
                        case 'n':
                            ParseLiteral("null");
                            return;
                        default:
                            ParseNumber();
                            return;
                    }
                }

                private void ParseObject(int depth)
                {
                    _index++;
                    SkipWhitespace();
                    if (TryConsume('}'))
                        return;

                    while (true)
                    {
                        if (_index >= _json.Length || _json[_index] != '"')
                            Fail();
                        ParseString();
                        SkipWhitespace();
                        Require(':');
                        SkipWhitespace();
                        ParseValue(depth + 1);
                        SkipWhitespace();
                        if (TryConsume('}'))
                            return;
                        Require(',');
                        SkipWhitespace();
                    }
                }

                private void ParseArray(int depth)
                {
                    _index++;
                    SkipWhitespace();
                    if (TryConsume(']'))
                        return;

                    while (true)
                    {
                        ParseValue(depth + 1);
                        SkipWhitespace();
                        if (TryConsume(']'))
                            return;
                        Require(',');
                        SkipWhitespace();
                    }
                }

                private void ParseString()
                {
                    Require('"');
                    while (_index < _json.Length)
                    {
                        var character = _json[_index++];
                        if (character == '"')
                            return;
                        if (character < 0x20)
                            Fail();
                        if (character != '\\')
                            continue;
                        if (_index >= _json.Length)
                            Fail();

                        var escape = _json[_index++];
                        switch (escape)
                        {
                            case '"':
                            case '\\':
                            case '/':
                            case 'b':
                            case 'f':
                            case 'n':
                            case 'r':
                            case 't':
                                break;
                            case 'u':
                                var codeUnit = ParseHexCodeUnit();
                                if (codeUnit >= 0xd800 && codeUnit <= 0xdbff)
                                {
                                    if (_index + 2 > _json.Length
                                        || _json[_index] != '\\'
                                        || _json[_index + 1] != 'u')
                                    {
                                        Fail();
                                    }
                                    _index += 2;
                                    var lowSurrogate = ParseHexCodeUnit();
                                    if (lowSurrogate < 0xdc00 || lowSurrogate > 0xdfff)
                                        Fail();
                                }
                                else if (codeUnit >= 0xdc00 && codeUnit <= 0xdfff)
                                {
                                    Fail();
                                }
                                break;
                            default:
                                Fail();
                                break;
                        }
                    }
                    Fail();
                }

                private void ParseLiteral(string literal)
                {
                    if (_index + literal.Length > _json.Length
                        || !string.Equals(
                            _json.Substring(_index, literal.Length),
                            literal,
                            StringComparison.Ordinal))
                    {
                        Fail();
                    }
                    _index += literal.Length;
                }

                private int ParseHexCodeUnit()
                {
                    var result = 0;
                    for (var digit = 0; digit < 4; digit++)
                    {
                        if (_index >= _json.Length)
                            Fail();
                        var character = _json[_index++];
                        result <<= 4;
                        if (character >= '0' && character <= '9')
                            result |= character - '0';
                        else if (character >= 'a' && character <= 'f')
                            result |= character - 'a' + 10;
                        else if (character >= 'A' && character <= 'F')
                            result |= character - 'A' + 10;
                        else
                            Fail();
                    }
                    return result;
                }

                private void ParseNumber()
                {
                    if (_index >= _json.Length || _json[_index] == '-')
                        Fail();

                    ulong value = 0;
                    if (TryConsume('0'))
                    {
                        if (_index < _json.Length && IsDigit(_json[_index]))
                            Fail();
                    }
                    else
                    {
                        if (_index >= _json.Length || !IsOneToNine(_json[_index]))
                            Fail();
                        do
                        {
                            var digit = (uint)(_json[_index] - '0');
                            if (value > (ulong.MaxValue - digit) / 10UL)
                                Fail();
                            value = value * 10UL + digit;
                            _index++;
                        } while (_index < _json.Length && IsDigit(_json[_index]));
                    }

                    if (_index < _json.Length
                        && (_json[_index] == '.'
                            || _json[_index] == 'e'
                            || _json[_index] == 'E'))
                    {
                        Fail();
                    }
                }

                private bool TryConsume(char expected)
                {
                    if (_index >= _json.Length || _json[_index] != expected)
                        return false;
                    _index++;
                    return true;
                }

                private void Require(char expected)
                {
                    if (!TryConsume(expected))
                        Fail();
                }

                private static bool IsDigit(char value)
                    => value >= '0' && value <= '9';

                private static bool IsOneToNine(char value)
                    => value >= '1' && value <= '9';

                private static void Fail()
                    => throw InvalidFrame("The U2R2 JSON header is invalid.");
            }
        }

        private static uint ReadUInt32(byte[] data, int offset)
            => (uint)(data[offset]
                      | data[offset + 1] << 8
                      | data[offset + 2] << 16
                      | data[offset + 3] << 24);

        private static void WriteUInt32(byte[] data, int offset, uint value)
        {
            data[offset] = (byte)value;
            data[offset + 1] = (byte)(value >> 8);
            data[offset + 2] = (byte)(value >> 16);
            data[offset + 3] = (byte)(value >> 24);
        }
    }
}
