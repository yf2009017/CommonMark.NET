﻿using CommonMark.Syntax;
using System;
using System.Collections.Generic;
using System.Text;

namespace CommonMark.Parser
{
    internal static class InlineMethods
    {
        private static readonly char[] WhiteSpaceCharacters = new[] { '\n', ' ' };
        private static readonly char[] BracketSpecialCharacters = new[] { '\\', ']', '[' };

        /// <summary>
        /// Initializes the array of delegates for inline parsing.
        /// </summary>
        /// <returns></returns>
        internal static Func<Parser.Subject, Syntax.Inline>[] InitializeParsers(CommonMarkSettings settings)
        {
            var strikethroughTilde = 0 != (settings.AdditionalFeatures & CommonMarkAdditionalFeatures.StrikethroughTilde);

            var p = new Func<Parser.Subject, Syntax.Inline>[strikethroughTilde ? 127 : 97];
            p['\n'] = handle_newline;
            p['`'] = handle_backticks;
            p['\\'] = handle_backslash;
            p['&'] = handle_entity;
            p['<'] = handle_pointy_brace;
            p['_'] = HandleEmphasis;
            p['*'] = HandleEmphasis;
            p['['] = HandleLeftSquareBracket;
            p[']'] = HandleRightSquareBracket;
            p['!'] = HandleExclamation;

            if (strikethroughTilde)
                p['~'] = HandleTilde;

            return p;
        }


        /// <summary>
        /// Collapses internal whitespace to single space, removes leading/trailing whitespace, folds case.
        /// </summary>
        private static string NormalizeReference(StringPart s)
        {
            if (s.Length == 0)
                return string.Empty;

            return NormalizeWhitespace(s.Source, s.StartIndex, s.Length).ToUpperInvariant();
        }

        // Returns reference if refmap contains a reference with matching
        // label, otherwise null.
        public static Reference lookup_reference(Dictionary<string, Reference> refmap, StringPart lab)
        {
            if (refmap == null)
                return null;

            if (lab.Length > Reference.MaximumReferenceLabelLength)
                return Reference.InvalidReference;

            string label = NormalizeReference(lab);

            Reference r;
            if (refmap.TryGetValue(label, out r))
                return r;

            return null;
        }

        public static Reference make_reference(StringPart label, string url, string title)
        {
            Reference r = new Reference();
            r.Label = NormalizeReference(label);
            r.Url = url;
            r.Title = title;
            return r;
        }

        public static void add_reference(Dictionary<string, Reference> refmap, Reference refer)
        {
            if (refmap.ContainsKey(refer.Label))
                return;

            refmap.Add(refer.Label, refer);
        }

        // Append inline list b to the end of inline list a.
        // Return pointer to head of new list.
        private static Inline append_inlines(Inline a, Inline b)
        {
            if (a == null)
            {  // null acts like an empty list
                return b;
            }

            a.LastSibling.NextSibling = b;
            return a;
        }

        // Make a 'subject' from an input string.
#if OptimizeFor45
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
#endif
        private static Subject make_subject(string s, Dictionary<string, Reference> refmap)
        {
            return new Subject(s.TrimEnd(), refmap);
        }

        // Return the next character in the subject, without advancing.
        // Return 0 if at the end of the subject.
#if OptimizeFor45
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
#endif
        private static char peek_char(Subject subj)
        {
            return subj.Buffer.Length <= subj.Position ? '\0' : subj.Buffer[subj.Position];
        }

        // Advance the subject.  Doesn't check for eof.
#if OptimizeFor45
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
#endif
        private static void advance(Subject subj)
        {
            subj.Position += 1;
        }

        // Try to process a backtick code span that began with a
        // span of ticks of length openticklength length (already
        // parsed).  Return 0 if you don't find matching closing
        // backticks, otherwise return the position in the subject
        // after the closing backticks.
        static int scan_to_closing_backticks(Subject subj, int openticklength)
        {
            // read non backticks
            var buf = subj.Buffer;
            var len = buf.Length;
            var cc = 0;
            var pos = subj.Position;

            for (var i = subj.Position; i < len; i++)
            {
                if (buf[i] == '`')
                {
                    cc++;
                }
                else
                {
                    if (cc == openticklength)
                    {
                        subj.Position = i;
                        return i;
                    }

                    i = buf.IndexOf('`', i) - 1;
                    if (i == -2)
                        return 0;

                    cc = 0;
                }
            }

            if (cc == openticklength)
            {
                subj.Position = len;
                return len;
            }

            return 0;
        }

        /// <summary>
        /// Collapses consecutive space and newline characters into a single space.
        /// Additionaly removes leading and trailing spaces.
        /// </summary>
        private static string NormalizeWhitespace(string s, int startIndex, int count)
        {
            char c;

            // count will actually be the lastIndex. The method argument is count only because other similar methods have startIndex/count
            count = startIndex + count - 1;

            // trim leading and trailing spaces.
            while (startIndex < count)
            {
                c = s[startIndex];
                if (c != ' ' && c != '\n') break;
                startIndex++;
            }

            while (count >= startIndex)
            {
                c = s[count];
                if (c != ' ' && c != '\n') break;
                count--;
            }

            if (count < startIndex)
                return string.Empty;

            // collapse inner whitespace
            // the complexity of this method is mainly so that the use of StringBuilder could be avoided if it is not needed
            StringBuilder sb = null;
            int pos = startIndex;
            int lastPos = startIndex;
            while (-1 != (pos = s.IndexOfAny(WhiteSpaceCharacters, pos, count - pos)))
            {
                if (s[pos] == '\n')
                {
                    if (sb == null)
                        sb = new StringBuilder(s.Length);

                    // newline has to be replaced with ' '
                    sb.Append(s, lastPos, pos - lastPos);
                    sb.Append(' ');

                    // move past consecutive spaces
                    do
                    {
                        c = s[++pos];
                        if (c != ' ' && c != '\n')
                            break;
                    } while (pos < count);

                    lastPos = pos;
                }
                else
                {
                    c = s[++pos];

                    if (c == ' ' || c == '\n')
                    {
                        // multiple consecutive whitespaces
                        if (sb == null)
                            sb = new StringBuilder(s.Length);

                        sb.Append(s, lastPos, pos - lastPos);

                        // move past consecutive spaces
                        do
                        {
                            c = s[++pos];
                            if (c != ' ' && c != '\n')
                                break;
                        } while (pos < count);

                        lastPos = pos;
                    }
                }
            }

            if (sb == null)
                return s.Substring(startIndex, count - startIndex + 1);

            sb.Append(s, lastPos, count - lastPos + 1);
            return sb.ToString();
        }

        // Parse backtick code section or raw backticks, return an inline.
        // Assumes that the subject has a backtick at the current position.
        static Inline handle_backticks(Subject subj)
        {
            int ticklength = 0;
            var bl = subj.Buffer.Length;
            while (subj.Position < bl && (subj.Buffer[subj.Position] == '`'))
            {
                ticklength++;
                subj.Position++;
            }

            int startpos = subj.Position;
            int endpos = scan_to_closing_backticks(subj, ticklength);
            if (endpos == 0)
            {
                // closing not found
                subj.Position = startpos; // rewind to right after the opening ticks
                return new Inline(new string('`', ticklength));
            }
            else
            {
                return new Inline(InlineTag.Code, NormalizeWhitespace(subj.Buffer, startpos, endpos - startpos - ticklength));
            }
        }

        /// <summary>
        /// Scans the subject for a series of the given emphasis character, testing if they could open and/or close
        /// an emphasis element.
        /// </summary>
        private static int ScanEmphasisDelimeters(Subject subj, char c, out bool can_open, out bool can_close)
        {
            int numdelims = 0;
            char char_before, char_after;
            int startpos = subj.Position;
            int len = subj.Buffer.Length;

            while (startpos + numdelims < len && subj.Buffer[startpos + numdelims] == c)
                numdelims++;

            if (numdelims == 0)
            {
                can_open = false;
                can_close = false;
                return numdelims;
            }

            char_before = startpos == 0 ? '\n' : subj.Buffer[startpos - 1];
            subj.Position = (startpos += numdelims);
            char_after = len == startpos ? '\n' : subj.Buffer[startpos];

            bool beforeIsSpace, beforeIsPunctuation, afterIsSpace, afterIsPunctuation;

            Utilities.CheckUnicodeCategory(char_before, out beforeIsSpace, out beforeIsPunctuation);
            Utilities.CheckUnicodeCategory(char_after, out afterIsSpace, out afterIsPunctuation);

            can_open = !afterIsSpace && !(afterIsPunctuation && !beforeIsSpace && !beforeIsPunctuation);
            can_close = !beforeIsSpace && !(beforeIsPunctuation && !afterIsSpace && !afterIsPunctuation);

            if (c == '_')
            {
                can_open = can_open && !Utilities.IsAsciiLetterOrDigit(char_before);
                can_close = can_close && !Utilities.IsAsciiLetterOrDigit(char_after);
            }

            return numdelims;
        }

        internal static int MatchEmphasisStack(InlineStack opener, Subject subj, int closingDelimeterCount, InlineStack closer)
        {
            // calculate the actual number of delimeters used from this closer
            int useDelims;
            var openerDelims = opener.DelimeterCount;
            if (closingDelimeterCount < 3 || openerDelims < 3)
                useDelims = closingDelimeterCount <= openerDelims ? closingDelimeterCount : openerDelims;
            else
                useDelims = closingDelimeterCount % 2 == 0 ? 2 : 1;

            if (openerDelims == useDelims)
            {
                // the opener is completely used up - remove the stack entry and reuse the inline element
                var inl = opener.StartingInline;
                inl.Tag = useDelims == 1 ? InlineTag.Emphasis : InlineTag.Strong;
                inl.LiteralContent = null;

                if (closer != null)
                {
                    inl.FirstChild = inl.NextSibling;
                    if ((closer.DelimeterCount -= useDelims) > 0)
                    {
                        // a new inline element must be created because the old one has to be the one that
                        // finalizes the children of the emphasis
                        var newCloserInline = new Inline(closer.StartingInline.LiteralContent.Substring(useDelims));
                        closer.StartingInline.LiteralContent = null;
                        closer.StartingInline.NextSibling = null;
                        inl.NextSibling = closer.StartingInline = newCloserInline;
                    }
                    else
                    {
                        closer.StartingInline.LiteralContent = null;
                        inl.NextSibling = closer.StartingInline.NextSibling;
                        closer.StartingInline.NextSibling = null;
                    }
                }
                else
                {
                    inl.FirstChild = inl.NextSibling;
                    inl.NextSibling = null;
                }

                InlineStack.RemoveStackEntry(opener, subj, closer);

                if (subj != null)
                    subj.LastInline = inl;
            }
            else
            {
                // the opener will only partially be used - stack entry remains (truncated) and a new inline is added.
                var inl = opener.StartingInline;
                opener.DelimeterCount -= useDelims;
                inl.LiteralContent = inl.LiteralContent.Substring(0, opener.DelimeterCount);

                var emph = new Inline(useDelims == 1 ? InlineTag.Emphasis : InlineTag.Strong, inl.NextSibling);
                inl.NextSibling = emph;

                if (subj != null)
                    subj.LastInline = emph;

                if (closer != null)
                {
                    // a new inline element must be created because the old one has to be the one that
                    // finalizes the children of the emphasis
                    var newCloserInline = new Inline(closer.StartingInline.LiteralContent.Substring(useDelims));
                    newCloserInline.NextSibling = closer.StartingInline.NextSibling;
                    closer.DelimeterCount -= useDelims;
                    closer.StartingInline.LiteralContent = null;
                    closer.StartingInline.NextSibling = null;
                    emph.NextSibling = closer.StartingInline = newCloserInline;
                }
            }

            return useDelims;
        }

        private static Inline HandleEmphasis(Subject subj)
        {
            bool can_open, can_close;
            var c = subj.Buffer[subj.Position];
            var numdelims = ScanEmphasisDelimeters(subj, c, out can_open, out can_close);

            if (can_close)
            {
                // walk the stack and find a matching opener, if there is one
                var istack = InlineStack.FindMatchingOpener(subj.LastPendingInline, InlineStack.InlineStackPriority.Emphasis, c, out can_close);
                if (istack != null)
                {
                    var useDelims = MatchEmphasisStack(istack, subj, numdelims, null);

                    // if the closer was not fully used, move back a char or two and try again.
                    if (useDelims < numdelims)
                    {
                        subj.Position = subj.Position - numdelims + useDelims;

                        // use recursion only if it will not be very deep.
                        if (numdelims < 10)
                            return HandleEmphasis(subj);
                    }

                    return null;
                }
            }

            var inlText = new Inline(subj.Buffer.Substring(subj.Position - numdelims, numdelims));

            if (can_open || can_close)
            {
                var istack = new InlineStack();
                istack.DelimeterCount = numdelims;
                istack.Delimeter = c;
                istack.StartingInline = inlText;
                istack.Priority = InlineStack.InlineStackPriority.Emphasis;
                istack.Flags = (can_open ? InlineStack.InlineStackFlags.Opener : 0)
                             | (can_close ? InlineStack.InlineStackFlags.Closer : 0);

                InlineStack.AppendStackEntry(istack, subj);
            }

            return inlText;
        }

        private static Inline HandleTilde(Subject subj)
        {
            bool can_open, can_close;
            var numdelims = ScanEmphasisDelimeters(subj, '~', out can_open, out can_close);

            if (numdelims == 1)
                return new Inline("~");

            if (can_close)
            {
                // walk the stack and find a matching opener, if there is one
                var istack = InlineStack.FindMatchingOpener(subj.LastPendingInline, InlineStack.InlineStackPriority.Emphasis, '~', out can_close);
                if (istack != null)
                {
                    MatchTildeStack(istack, subj, null);

                    // if the closer was not fully used, move back a char or two and try again.
                    if (numdelims > 2)
                    {
                        subj.Position = subj.Position - numdelims + 2;

                        // use recursion only if it will not be very deep.
                        if (numdelims < 10)
                            return HandleTilde(subj);
                    }

                    return null;
                }
            }

            var inlText = new Inline(subj.Buffer.Substring(subj.Position - numdelims, numdelims));

            if (can_open || can_close)
            {
                var istack = new InlineStack();
                istack.DelimeterCount = numdelims;
                istack.Delimeter = '~';
                istack.StartingInline = inlText;
                istack.Priority = InlineStack.InlineStackPriority.Emphasis;
                istack.Flags = (can_open ? InlineStack.InlineStackFlags.Opener : 0)
                             | (can_close ? InlineStack.InlineStackFlags.Closer : 0);

                InlineStack.AppendStackEntry(istack, subj);
            }

            return inlText;
        }

        internal static void MatchTildeStack(InlineStack opener, Subject subj, InlineStack closer)
        {
            // calculate the actual number of delimeters used from this closer

            if (opener.DelimeterCount == 2)
            {
                // the opener is completely used up - remove the stack entry and reuse the inline element
                var inl = opener.StartingInline;
                inl.Tag = InlineTag.Strikethrough;
                inl.LiteralContent = null;

                if (closer != null)
                {
                    inl.FirstChild = inl.NextSibling;
                    if ((closer.DelimeterCount -= 2) > 0)
                    {
                        // a new inline element must be created because the old one has to be the one that
                        // finalizes the children of the strikethrough
                        var newCloserInline = new Inline(closer.StartingInline.LiteralContent.Substring(2));
                        closer.StartingInline.LiteralContent = null;
                        closer.StartingInline.NextSibling = null;
                        inl.NextSibling = closer.StartingInline = newCloserInline;
                    }
                    else
                    {
                        closer.StartingInline.LiteralContent = null;
                        inl.NextSibling = closer.StartingInline.NextSibling;
                        closer.StartingInline.NextSibling = null;
                    }
                }
                else
                {
                    inl.FirstChild = inl.NextSibling;
                    inl.NextSibling = null;
                }

                InlineStack.RemoveStackEntry(opener, subj, closer);

                if (subj != null)
                    subj.LastInline = inl;
            }
            else
            {
                // the opener will only partially be used - stack entry remains (truncated) and a new inline is added.
                var inl = opener.StartingInline;
                opener.DelimeterCount -= 2;
                inl.LiteralContent = opener.StartingInline.LiteralContent.Substring(0, opener.DelimeterCount);

                var strk = new Inline(InlineTag.Strikethrough, inl.NextSibling);
                inl.NextSibling = strk;

                if (subj != null)
                    subj.LastInline = strk;

                if (closer != null)
                {
                    // a new inline element must be created because the old one has to be the one that
                    // finalizes the children of the emphasis
                    var newCloserInline = new Inline(closer.StartingInline.LiteralContent.Substring(2));
                    newCloserInline.NextSibling = closer.StartingInline.NextSibling;
                    closer.DelimeterCount -= 2;
                    closer.StartingInline.LiteralContent = null;
                    closer.StartingInline.NextSibling = null;
                    strk.NextSibling = closer.StartingInline = newCloserInline;
                }
            }
        }

        private static Inline HandleExclamation(Subject subj)
        {
            advance(subj);
            if (peek_char(subj) == '[')
                return HandleLeftSquareBracket(subj, true);
            else
                return new Inline("!");
        }

        private static Inline HandleLeftSquareBracket(Subject subj)
        {
            return HandleLeftSquareBracket(subj, false);
        }

        private static Inline HandleLeftSquareBracket(Subject subj, bool isImage)
        {
            // move past the '['
            subj.Position++;

            var inlText = new Inline(isImage ? "![" : "[");

            var istack = new InlineStack();
            istack.Delimeter = '[';
            istack.StartingInline = inlText;
            istack.StartPosition = subj.Position;
            istack.Priority = InlineStack.InlineStackPriority.Links;
            istack.Flags = InlineStack.InlineStackFlags.Opener | (isImage ? InlineStack.InlineStackFlags.ImageLink : InlineStack.InlineStackFlags.None);

            InlineStack.AppendStackEntry(istack, subj);

            return inlText;
        }

        internal static void MatchSquareBracketStack(InlineStack opener, Subject subj, InlineStack closer, Reference details)
        {
            if (details != null)
            {
                var inl = opener.StartingInline;
                var isImage = 0 != (opener.Flags & InlineStack.InlineStackFlags.ImageLink);
                inl.Tag = isImage ? InlineTag.Image : InlineTag.Link;
                inl.FirstChild = inl.NextSibling;
                inl.NextSibling = null;

                inl.TargetUrl = details.Url;
                inl.LiteralContent = details.Title;

                if (!isImage)
                {
                    // since there cannot be nested links, remove any other link openers before this
                    var temp = opener.Previous;
                    while (temp != null && temp.Priority <= InlineStack.InlineStackPriority.Links)
                    {
                        if (temp.Delimeter == '[' && temp.Flags == opener.Flags)
                        {
                            // mark the previous entries as "inactive"
                            if (temp.DelimeterCount == -1)
                                break;

                            temp.DelimeterCount = -1;
                        }

                        temp = temp.Previous;
                    }
                }

                InlineStack.RemoveStackEntry(opener, subj, closer);

                if (subj != null)
                    subj.LastInline = inl;
            }
            else
            {
                // this looked like a link, but was not.
                // remove the opener and closer stack entries but leave the inbetween intact
                InlineStack.RemoveStackEntry(opener, subj, opener);

                if (closer != null)
                    InlineStack.RemoveStackEntry(closer, subj, closer);
                else
                {
                    var inl = new Inline("]");
                    subj.LastInline.LastSibling.NextSibling = inl;
                    subj.LastInline = inl;
                }
            }
        }

        private static Inline HandleRightSquareBracket(Subject subj)
        {
            // move past ']'
            subj.Position++;

            bool can_close;
            var istack = InlineStack.FindMatchingOpener(subj.LastPendingInline, InlineStack.InlineStackPriority.Links, '[', out can_close);

            if (istack != null)
            {
                // if the opener is "inactive" then it means that there was a nested link
                if (istack.DelimeterCount == -1)
                {
                    InlineStack.RemoveStackEntry(istack, subj, istack);
                    return new Inline("]");
                }

                var endpos = subj.Position;

                // try parsing details for '[foo](/url "title")' or '[foo][bar]'
                var details = ParseLinkDetails(subj);

                // try lookup of the brackets themselves
                if (details == null || details == Reference.SelfReference)
                {
                    var startpos = istack.StartPosition;
                    var label = new StringPart(subj.Buffer, startpos, endpos - startpos - 1);

                    details = lookup_reference(subj.ReferenceMap, label);
                }

                if (details == Reference.InvalidReference)
                    details = null;

                MatchSquareBracketStack(istack, subj, null, details);
                return null;
            }

            var inlText = new Inline("]");

            if (can_close)
            {
                // note that the current implementation will not work if there are other inlines with priority
                // higher than Links.
                // to fix this the parsed link details should be added to the closer element in the stack.

                throw new NotSupportedException("It is not supported to have inline stack priority higher than Links.");

                ////istack = new InlineStack();
                ////istack.Delimeter = '[';
                ////istack.StartingInline = inlText;
                ////istack.StartPosition = subj.Position;
                ////istack.Priority = InlineStack.InlineStackPriority.Links;
                ////istack.Flags = InlineStack.InlineStackFlags.Closer;

                ////InlineStack.AppendStackEntry(istack, subj);
            }

            return inlText;
        }

        // Parse backslash-escape or just a backslash, returning an inline.
        private static Inline handle_backslash(Subject subj)
        {
            advance(subj);

            if (subj.Position >= subj.Buffer.Length)
                return new Inline("\\");

            var nextChar = subj.Buffer[subj.Position];

            if (Utilities.IsEscapableSymbol(nextChar))
            {
                // only ascii symbols and newline can be escaped
                // the exception is the unicode bullet char since it can be used for defining list items
                advance(subj);
                return new Inline(nextChar.ToString());
            }
            else if (nextChar == '\n')
            {
                advance(subj);
                return new Inline(InlineTag.LineBreak);
            }
            else
            {
                return new Inline("\\");
            }
        }

        // Parse an entity or a regular "&" string.
        // Assumes the subject has an '&' character at the current position.
        private static Inline handle_entity(Subject subj)
        {
            int match;
            string namedEntity;
            int numericEntity;
            match = Scanner.scan_entity(subj.Buffer, subj.Position, subj.Buffer.Length - subj.Position, out namedEntity, out numericEntity);
            if (match > 0)
            {
                subj.Position += match;

                if (namedEntity != null)
                {
                    var decoded = EntityDecoder.DecodeEntity(namedEntity);
                    if (decoded != null)
                        return new Inline(decoded);
                }
                else if (numericEntity > 0)
                {
                    var decoded = EntityDecoder.DecodeEntity(numericEntity);
                    if (decoded != null)
                        return new Inline(decoded);
                    return new Inline("\uFFFD");
                }

                return new Inline(subj.Buffer.Substring(subj.Position - match, match));
            }
            else
            {
                advance(subj);
                return new Inline("&");
            }
        }

        /// <summary>
        /// Creates a new <see cref="Inline"/> element that represents string content but the given content
        /// is processed to decode any HTML entities in it.
        /// </summary>
        static Inline ParseStringEntities(string s)
        {
            Inline result = null;
            Inline inew;
            int searchpos;
            char c;
            Subject subj = make_subject(s, null);

            while ('\0' != (c = peek_char(subj)))
            {
                if (c == '&')
                {
                    inew = handle_entity(subj);
                }
                else
                {
                    searchpos = subj.Buffer.IndexOf('&', subj.Position);
                    if (searchpos == -1)
                        searchpos = subj.Buffer.Length;

                    inew = new Inline(subj.Buffer.Substring(subj.Position, searchpos - subj.Position));
                    subj.Position = searchpos;
                }

                result = append_inlines(result, inew);
            }

            return result;
        }

        /// <summary>
        /// Destructively unescape a string: remove backslashes before punctuation or symbol characters.
        /// </summary>
        /// <param name="url">The string data that will be changed by unescaping any punctuation or symbol characters.</param>
        public static string Unescape(string url)
        {
            // remove backslashes before punctuation chars:
            int searchPos = 0;
            int lastPos = 0;
            int match;
            char c;
            char[] search = new[] { '\\', '&' };
            StringBuilder sb = null;

            while ((searchPos = url.IndexOfAny(search, searchPos)) != -1)
            {
                c = url[searchPos];
                if (c == '\\')
                {
                    searchPos++;

                    if (url.Length == searchPos)
                        break;

                    c = url[searchPos];
                    if (Utilities.IsEscapableSymbol(c))
                    {
                        if (sb == null) sb = new StringBuilder(url.Length);
                        sb.Append(url, lastPos, searchPos - lastPos - 1);
                        lastPos = searchPos;
                    }
                }
                else if (c == '&')
                {
                    string namedEntity;
                    int numericEntity;
                    match = Scanner.scan_entity(url, searchPos, url.Length - searchPos, out namedEntity, out numericEntity);
                    if (match == 0)
                    {
                        searchPos++;
                    }
                    else
                    {
                        searchPos += match;

                        if (namedEntity != null)
                        {
                            var decoded = EntityDecoder.DecodeEntity(namedEntity);
                            if (decoded != null)
                            {
                                if (sb == null) sb = new StringBuilder(url.Length);
                                sb.Append(url, lastPos, searchPos - match - lastPos);
                                sb.Append(decoded);
                                lastPos = searchPos;
                            }
                        }
                        else if (numericEntity > 0)
                        {
                            var decoded = EntityDecoder.DecodeEntity(numericEntity);
                            if (decoded != null)
                            {
                                if (sb == null) sb = new StringBuilder(url.Length);
                                sb.Append(url, lastPos, searchPos - match - lastPos);
                                sb.Append(decoded);
                            }
                            else
                            {
                                if (sb == null) sb = new StringBuilder(url.Length);
                                sb.Append(url, lastPos, searchPos - match - lastPos);
                                sb.Append('\uFFFD');
                            }

                            lastPos = searchPos;
                        }
                    }
                }
            }

            if (sb == null)
                return url;

            sb.Append(url, lastPos, url.Length - lastPos);
            return sb.ToString();
        }

        /// <summary>
        /// Clean a URL: remove surrounding whitespace and surrounding &lt; &gt; and remove \ that escape punctuation and other symbols.
        /// </summary>
        /// <remarks>Original: clean_url(ref string)</remarks>
        private static string CleanUrl(string url)
        {
            if (url.Length == 0)
                return url;

            // remove surrounding <> if any:
            url = url.Trim();

            if (url[0] == '<' && url[url.Length - 1] == '>')
                url = url.Substring(1, url.Length - 2);

            return Unescape(url);
        }

        /// <summary>
        /// Clean a title: remove surrounding quotes and remove \ that escape punctuation.
        /// </summary>
        /// <remarks>Original: clean_title(ref string)</remarks>
        private static string CleanTitle(string title)
        {
            // remove surrounding quotes if any:
            int titlelength = title.Length;
            if (titlelength == 0)
                return title;

            var a = title[0];
            var b = title[titlelength - 1];
            if ((a == '\'' && b == '\'') || (a == '(' && b == ')') || (a == '"' && b == '"'))
                title = title.Substring(1, titlelength - 2);

            return Unescape(title);
        }

        // Parse an autolink or HTML tag.
        // Assumes the subject has a '<' character at the current position.
        static Inline handle_pointy_brace(Subject subj)
        {
            int matchlen = 0;
            string contents;
            Inline result;

            // advance past first <
            advance(subj);  

            // first try to match a URL autolink
            matchlen = Scanner.scan_autolink_uri(subj.Buffer, subj.Position);
            if (matchlen > 0)
            {
                contents = subj.Buffer.Substring(subj.Position, matchlen - 1);
                subj.Position += matchlen;
                result = Inline.CreateLink(ParseStringEntities(contents), contents, string.Empty);
                return result;
            }

            // next try to match an email autolink
            matchlen = Scanner.scan_autolink_email(subj.Buffer, subj.Position);
            if (matchlen > 0)
            {
                contents = subj.Buffer.Substring(subj.Position, matchlen - 1);
                subj.Position += matchlen;
                result = Inline.CreateLink(ParseStringEntities(contents), "mailto:" + contents, string.Empty);
                return result;
            }

            // finally, try to match an html tag
            matchlen = Scanner.scan_html_tag(subj.Buffer, subj.Position);
            if (matchlen > 0)
            {
                contents = subj.Buffer.Substring(subj.Position - 1, matchlen + 1);
                subj.Position += matchlen;
                return new Inline(InlineTag.RawHtml, contents);
            }
            else
            {
                // if nothing matches, just return the opening <:
                return new Inline("<");
            }
        }

        // Parse a link or the link portion of an image, or return a fallback.
        static Reference ParseLinkDetails(Subject subj)
        {
            int n;
            int sps;
            int endlabel, starturl, endurl, starttitle, endtitle, endall;
            string url, title;
            endlabel = subj.Position;

            var c = peek_char(subj);

            if (c == '(' &&
                    ((sps = Scanner.scan_spacechars(subj.Buffer, subj.Position + 1)) > -1) &&
                    ((n = Scanner.scan_link_url(subj.Buffer, subj.Position + 1 + sps)) > -1))
            {
                // try to parse an explicit link:
                starturl = subj.Position + 1 + sps; // after (
                endurl = starturl + n;
                starttitle = endurl + Scanner.scan_spacechars(subj.Buffer, endurl);
                // ensure there are spaces btw url and title
                endtitle = (starttitle == endurl) ? starttitle :
                           starttitle + Scanner.scan_link_title(subj.Buffer, starttitle);
                endall = endtitle + Scanner.scan_spacechars(subj.Buffer, endtitle);
                if (endall < subj.Buffer.Length && subj.Buffer[endall] == ')')
                {
                    subj.Position = endall + 1;
                    url = subj.Buffer.Substring(starturl, endurl - starturl);
                    url = CleanUrl(url);
                    title = subj.Buffer.Substring(starttitle, endtitle - starttitle);
                    title = CleanTitle(title);

                    return new Reference() { Title = title, Url = url };
                }
            }
            else if (c == '[' || c == ' ' || c == '\n')
            {
                var label = ParseReferenceLabel(subj);
                if (label != null)
                {
                    if (label.Value.Length == 0)
                        return Reference.SelfReference;

                    var details = lookup_reference(subj.ReferenceMap, label.Value);
                    if (details != null)
                        return details;

                    // rollback the subject but return InvalidReference so that the caller knows not to
                    // parse 'foo' from [foo][bar].
                    subj.Position = endlabel;
                    return Reference.InvalidReference;
                }
            }

            // rollback the subject position because didn't match anything.
            subj.Position = endlabel;
            return null;
        }

        // Parse a hard or soft linebreak, returning an inline.
        // Assumes the subject has a newline at the current position.
        static Inline handle_newline(Subject subj)
        {
            int nlpos = subj.Position;

            // skip over newline
            advance(subj);

            // skip spaces at beginning of line
            var len = subj.Buffer.Length;
            while (subj.Position < len && subj.Buffer[subj.Position] == ' ')
                advance(subj);

            if (nlpos > 1 && subj.Buffer[nlpos - 1] == ' ' && subj.Buffer[nlpos - 2] == ' ')
                return new Inline(InlineTag.LineBreak);
            else
                return new Inline(InlineTag.SoftBreak);
        }

        // Parse an inline, advancing subject, and add it to last element.
        // Adjust tail to point to new last element of list.
        // Return 0 if no inline can be parsed, 1 otherwise.
        public static Inline parse_inline(Subject subj, Func<Subject, Inline>[] parsers, char[] specialCharacters)
        {
            var c = subj.Buffer[subj.Position];

            var parser = c < parsers.Length ? parsers[c] : null;

            if (parser != null)
                return parser(subj);

            string contents;

            // we read until we hit a special character
            var endpos = subj.Buffer.IndexOfAny(specialCharacters, subj.Position);

            if (endpos == subj.Position)
            {
                // current char is special: read a 1-character str
                contents = subj.Buffer[endpos].ToString();
                advance(subj);
            }
            else if (endpos == -1)
            {
                // special char not found, take whole rest of buffer:
                endpos = subj.Buffer.Length;
                contents = subj.Buffer.Substring(subj.Position);
                subj.Position = endpos;
            }
            else
            {
                // take buffer from subj.pos to endpos to str.
                contents = subj.Buffer.Substring(subj.Position, endpos - subj.Position);

                subj.Position = endpos;
                // if we're at a newline, strip trailing spaces.
                if (peek_char(subj) == '\n')
                    contents = contents.TrimEnd();
            }

            return new Inline(contents);
        }

        public static Inline parse_inlines(string input, Dictionary<string, Reference> refmap, Func<Subject, Inline>[] parsers, char[] specialCharacters)
        {
            if (input == null)
                return null;

            Subject subj = make_subject(input, refmap);

            var len = subj.Buffer.Length;

            if (len == 0)
                return null;

            var first = parse_inline(subj, parsers, specialCharacters);
            subj.LastInline = first.LastSibling;

            Inline cur;
            while (subj.Position < len)
            {
                cur = parse_inline(subj, parsers, specialCharacters);
                if (cur != null)
                {
                    subj.LastInline.NextSibling = cur;
                    subj.LastInline = cur.LastSibling;
                }
            }

            InlineStack.PostProcessInlineStack(subj, subj.FirstPendingInline, subj.LastPendingInline, InlineStack.InlineStackPriority.Maximum);

            return first;
        }

        // Parse zero or more space characters, including at most one newline.
        private static void spnl(Subject subj)
        {
            bool seen_newline = false;
            var len = subj.Buffer.Length;
            char c;
            while (subj.Position < len)
            {
                c = subj.Buffer[subj.Position];
                if (c == ' ' || (!seen_newline && (seen_newline = c == '\n')))
                    advance(subj);
                else
                    return;
            }
        }

        /// <summary>
        /// Parses the contents of [..] for a reference label. Only used for parsing 
        /// reference definition labels for use with the reference dictionary because 
        /// it does not properly parse nested inlines.
        /// 
        /// Assumes the source starts with '[' character or spaces before '['.
        /// Returns null and does not advance if no matching ] is found.
        /// Note the precedence:  code backticks have precedence over label bracket
        /// markers, which have precedence over *, _, and other inline formatting
        /// markers. So, 2 below contains a link while 1 does not:
        /// 1. [a link `with a ](/url)` character
        /// 2. [a link *with emphasized ](/url) text*        /// </summary>
        private static StringPart? ParseReferenceLabel(Subject subj)
        {
            var startPos = subj.Position;
            var source = subj.Buffer;
            var len = source.Length;

            char c = '\0';
            while (subj.Position < len)
            {
                c = subj.Buffer[subj.Position];
                if (c == ' ' || c == '\n')
                {
                    subj.Position++;
                    continue;
                }
                else if (c == '[')
                {
                    subj.Position++;
                    break;
                }
                else
                {
                    subj.Position = startPos;
                    return null;
                }
            }

            var labelStartPos = subj.Position;

            len = subj.Position + Reference.MaximumReferenceLabelLength;
            if (len > source.Length)
                len = source.Length;

            subj.Position = source.IndexOfAny(BracketSpecialCharacters, subj.Position, len - subj.Position);
            while (subj.Position > -1)
            {
                c = source[subj.Position];
                if (c == '\\')
                {
                    subj.Position += 2;
                    if (subj.Position >= len)
                        break;

                    subj.Position = source.IndexOfAny(BracketSpecialCharacters, subj.Position, len - subj.Position);
                }
                else if (c == '[')
                {
                    break;
                }
                else
                {
                    var label = new StringPart(source, labelStartPos, subj.Position - labelStartPos);
                    subj.Position++;
                    return label;
                }
            }

            subj.Position = startPos;
            return null;
        }

        // Parse reference.  Assumes string begins with '[' character.
        // Modify refmap if a reference is encountered.
        // Return 0 if no reference found, otherwise position of subject
        // after reference is parsed.
        public static int ParseReference(Syntax.StringContent input, Dictionary<string, Reference> refmap)
        {
            Subject subj = make_subject(input.ToString(), null);
            string url = null;
            string title = null;
            int matchlen = 0;
            int beforetitle;

            // parse label:
            var lab = ParseReferenceLabel(subj);
            if (lab == null || lab.Value.Length > Reference.MaximumReferenceLabelLength)
                return 0;

            // colon:
            if (peek_char(subj) == ':')
                advance(subj);
            else
                return 0;

            // parse link url:
            spnl(subj);
            matchlen = Scanner.scan_link_url(subj.Buffer, subj.Position);
            if (matchlen == 0)
                return 0;

            url = subj.Buffer.Substring(subj.Position, matchlen);
            url = CleanUrl(url);
            subj.Position += matchlen;

            // parse optional link_title
            beforetitle = subj.Position;
            spnl(subj);
            matchlen = Scanner.scan_link_title(subj.Buffer, subj.Position);
            if (matchlen > 0)
            {
                title = subj.Buffer.Substring(subj.Position, matchlen);
                title = CleanTitle(title);
                subj.Position += matchlen;
            }
            else
            {
                subj.Position = beforetitle;
                title = string.Empty;
            }

            // parse final spaces and newline:
            while (peek_char(subj) == ' ')
                advance(subj);

            if (peek_char(subj) == '\n')
                advance(subj);
            else if (peek_char(subj) != '\0')
                return 0;

            // insert reference into refmap
            add_reference(refmap, make_reference(lab.Value, url, title));

            return subj.Position;
        }
    }
}
