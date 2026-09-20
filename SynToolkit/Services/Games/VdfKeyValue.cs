#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace SynToolkit.Services.Games
{
    /// <summary>
    /// Minimal Valve KeyValues (VDF/ACF) text parser adapted from the subset of SteamKit2
    /// KeyValue behavior used by Playnite's SteamLocalService.
    /// </summary>
    internal sealed class VdfKeyValue
    {
        public string Name { get; set; } = string.Empty;
        public string? Value { get; set; }
        public List<VdfKeyValue> Children { get; } = new();

        public VdfKeyValue this[string key]
        {
            get
            {
                VdfKeyValue? child = Children.Find(c =>
                    string.Equals(c.Name, key, StringComparison.OrdinalIgnoreCase));
                if (child is not null)
                {
                    return child;
                }

                var empty = new VdfKeyValue { Name = key };
                Children.Add(empty);
                return empty;
            }
        }

        public uint AsUnsignedInteger()
        {
            return uint.TryParse(Value, out uint parsed) ? parsed : 0u;
        }

        public static VdfKeyValue ReadAsText(Stream stream)
        {
            using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
            return Parse(reader);
        }

        private static VdfKeyValue Parse(TextReader reader)
        {
            var root = new VdfKeyValue { Name = "root" };
            var stack = new Stack<VdfKeyValue>();
            stack.Push(root);

            while (true)
            {
                string? token = ReadToken(reader);
                if (token is null)
                {
                    break;
                }

                if (token == "}")
                {
                    if (stack.Count > 1)
                    {
                        stack.Pop();
                    }

                    continue;
                }

                string name = token;
                string? next = ReadToken(reader);
                if (next is null)
                {
                    break;
                }

                var current = stack.Peek();
                if (next == "{")
                {
                    var child = new VdfKeyValue { Name = name };
                    current.Children.Add(child);
                    stack.Push(child);
                }
                else
                {
                    current.Children.Add(new VdfKeyValue { Name = name, Value = next });
                }
            }

            return root.Children.Count == 1 ? root.Children[0] : root;
        }

        private static string? ReadToken(TextReader reader)
        {
            int ch;
            while ((ch = reader.Read()) != -1)
            {
                if (char.IsWhiteSpace((char)ch))
                {
                    continue;
                }

                if (ch == '/')
                {
                    int peek = reader.Peek();
                    if (peek == '/')
                    {
                        reader.Read();
                        while ((ch = reader.Read()) != -1 && ch != '\n')
                        {
                        }

                        continue;
                    }
                }

                if (ch == '"' )
                {
                    var sb = new StringBuilder();
                    while ((ch = reader.Read()) != -1)
                    {
                        if (ch == '\\')
                        {
                            int escaped = reader.Read();
                            if (escaped == -1)
                            {
                                break;
                            }

                            sb.Append((char)escaped);
                            continue;
                        }

                        if (ch == '"')
                        {
                            break;
                        }

                        sb.Append((char)ch);
                    }

                    return sb.ToString();
                }

                if (ch is '{' or '}')
                {
                    return ((char)ch).ToString();
                }

                var bare = new StringBuilder();
                bare.Append((char)ch);
                while (true)
                {
                    int peek = reader.Peek();
                    if (peek == -1 || char.IsWhiteSpace((char)peek) || peek is '{' or '}' or '"')
                    {
                        break;
                    }

                    bare.Append((char)reader.Read());
                }

                return bare.ToString();
            }

            return null;
        }
    }
}
