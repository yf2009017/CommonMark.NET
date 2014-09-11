﻿using CommonMark.Syntax;
using System;
using System.Collections.Generic;
using System.Text;

namespace CommonMark.Parser
{
    internal static class BlockMethods
    {
        private const int CODE_INDENT = 4;

        private static Block make_block(BlockTag tag, int start_line, int start_column)
        {
            Block e = new Block();
            e.Tag = tag;
            e.IsOpen = true;
            e.IsLastLineBlank = false;
            e.StartLine = start_line;
            e.StartColumn = start_column;
            e.EndLine = start_line;
            e.FirstChild = null;
            e.LastChild = null;
            e.Parent = null;
            e.Top = null;
            e.Attributes.ReferenceMap = null;
            e.StringContent = string.Empty;
            e.InlineContent = null;
            e.Next = null;
            e.Previous = null;
            return e;
        }

        // Create a root document block.
        public static Block make_document()
        {
            Block e = make_block(BlockTag.Document, 1, 1);
            e.Attributes.ReferenceMap = new Dictionary<string, Reference>();
            e.Top = e;
            return e;
        }

        // Returns true if line has only space characters, else false.
        private static bool is_blank(string s, int offset)
        {
            char? c;
            while (null != (c = BString.bchar(s, offset)))
            {
                if (c == '\n')
                {
                    return true;
                }
                else if (c == ' ')
                {
                    offset++;
                }
                else
                {
                    return false;
                }
            }
            return true;
        }

        private static bool can_contain(BlockTag parent_type, BlockTag child_type)
        {
            return (parent_type == BlockTag.Document ||
                     parent_type == BlockTag.BlockQuote ||
                     parent_type == BlockTag.ListItem ||
                     (parent_type == BlockTag.List && child_type == BlockTag.ListItem));
        }

        private static bool accepts_lines(BlockTag block_type)
        {
            return (block_type == BlockTag.Paragraph ||
                    block_type == BlockTag.AtxHeader ||
                    block_type == BlockTag.IndentedCode ||
                    block_type == BlockTag.FencedCode);
        }


        static void add_line(Block block, string ln, int offset)
        {
            string s;
            var len = ln.Length - offset;
            if (len < 0)
                s = string.Empty;
            else
                s = BString.bmidstr(ln, offset, len);

            if (!block.IsOpen)
                throw new CommonMarkException(string.Format("Attempted to add line '{0}' to closed container ({1}).", ln, block.Tag));

            block.StringContent += s;
        }

        /// <remarks>Original: remove_trailing_blank_lines(ref string)</remarks>
        private static string RemoveTrailingBlankLines(string ln, bool keepLastNewline)
        {
            string tofind = " \t\r\n";
            // find last nonspace:
            var pos = BString.bninchrr(ln, ln.Length - 1, tofind);
            if (pos == -1)
            { 
                // all spaces
                return string.Empty;
            }
            else
            {
                // find next newline after it
                pos = BString.bstrchrp(ln, '\n', pos);
                if (pos != -1)
                {
                    if (keepLastNewline)
                        pos++;

                    return ln.Remove(pos, ln.Length - pos);
                }
            }

            return ln;
        }
        // Check to see if a block ends with a blank line, descending
        // if needed into lists and sublists.
        static bool ends_with_blank_line(Block block)
        {
            if (block.IsLastLineBlank)
            {
                return true;
            }
            if ((block.Tag == BlockTag.List || block.Tag == BlockTag.ListItem) && block.LastChild != null)
            {
                return ends_with_blank_line(block.LastChild);
            }
            else
            {
                return false;
            }
        }

        // Break out of all containing lists
        private static void break_out_of_lists(ref Block bptr, int line_number)
        {
            Block container = bptr;
            Block b = container.Top;
            // find first containing list:
            while (b != null && b.Tag != BlockTag.List)
            {
                b = b.LastChild;
            }
            if (b != null)
            {
                while (container != null && container != b)
                {
                    finalize(container, line_number);
                    container = container.Parent;
                }
                finalize(b, line_number);
                bptr = b.Parent;
            }
        }


        public static void finalize(Block b, int line_number)
        {
            int firstlinelen;
            int pos;
            Block item;
            Block subitem;

            if (b == null)
                throw new ArgumentNullException("b");

            if (!b.IsOpen)
            {
                // don't do anything if the block is already closed
                return; 
            }

            b.IsOpen = false;
            if (line_number > b.StartLine)
            {
                b.EndLine = line_number - 1;
            }
            else
            {
                b.EndLine = line_number;
            }

            switch (b.Tag)
            {

                case BlockTag.Paragraph:
                    pos = 0;
                    while (BString.bchar(b.StringContent, 0) == '[' &&
                           0 != (pos = InlineMethods.parse_reference(b.StringContent,
                                                  b.Top.Attributes.ReferenceMap)))
                    {
                        b.StringContent = b.StringContent.Remove(0, pos);
                    }
                    if (is_blank(b.StringContent, 0))
                    {
                        b.Tag = BlockTag.ReferenceDefinition;
                    }
                    break;

                case BlockTag.IndentedCode:
                    b.StringContent = RemoveTrailingBlankLines(b.StringContent, true);
                    break;

                case BlockTag.FencedCode:
                    // first line of contents becomes info
                    firstlinelen = BString.bstrchr(b.StringContent, '\n');
                    b.Attributes.FencedCodeData.Info = BString.bmidstr(b.StringContent, 0, firstlinelen);
                    b.StringContent = b.StringContent.Remove(0, firstlinelen + 1); // +1 for \n
                    b.Attributes.FencedCodeData.Info = b.Attributes.FencedCodeData.Info.Trim();
                    b.Attributes.FencedCodeData.Info = InlineMethods.Unescape(b.Attributes.FencedCodeData.Info);
                    break;

                case BlockTag.List: // determine tight/loose status
                    b.Attributes.ListData.IsTight = true; // tight by default
                    item = b.FirstChild;

                    while (item != null)
                    {
                        // check for non-final non-empty list item ending with blank line:
                        if (item.IsLastLineBlank && item.Next != null)
                        {
                            b.Attributes.ListData.IsTight = false;
                            break;
                        }
                        // recurse into children of list item, to see if there are
                        // spaces between them:
                        subitem = item.FirstChild;
                        while (subitem != null)
                        {
                            if (ends_with_blank_line(subitem) &&
                                (item.Next != null || subitem.Next != null))
                            {
                                b.Attributes.ListData.IsTight = false;
                                break;
                            }
                            subitem = subitem.Next;
                        }
                        if (!(b.Attributes.ListData.IsTight))
                        {
                            break;
                        }
                        item = item.Next;
                    }

                    break;

                default:
                    break;
            }
        }

        // Add a block as child of another.  Return pointer to child.
        public static Block add_child(Block parent, BlockTag block_type, int start_line, int start_column)
        {
            // if 'parent' isn't the kind of block that can accept this child,
            // then back up til we hit a block that can.
            while (!can_contain(parent.Tag, block_type))
            {
                finalize(parent, start_line);
                parent = parent.Parent;
            }

            if (parent == null)
                throw new ArgumentNullException("parent");

            Block child = make_block(block_type, start_line, start_column);
            child.Parent = parent;
            child.Top = parent.Top;

            if (parent.LastChild != null)
            {
                parent.LastChild.Next = child;
                child.Previous = parent.LastChild;
            }
            else
            {
                parent.FirstChild = child;
                child.Previous = null;
            }
            parent.LastChild = child;
            return child;
        }


        // Walk through block and all children, recursively, parsing
        // string content into inline content where appropriate.
        public static void process_inlines(Block cur, Dictionary<string, Reference> refmap)
        {
            switch (cur.Tag)
            {

                case BlockTag.Paragraph:
                case BlockTag.AtxHeader:
                case BlockTag.SETextHeader:
                    if (cur.StringContent == null)
                        throw new CommonMarkException("The block does not contain string content.", cur);

                    cur.InlineContent = InlineMethods.parse_inlines(cur.StringContent, refmap);
                    cur.StringContent = null;
                    break;

                default:
                    break;
            }

            Block child = cur.FirstChild;
            while (child != null)
            {
                process_inlines(child, refmap);
                child = child.Next;
            }
        }

        /// <summary>
        /// Attempts to parse a list item marker (bullet or enumerated).
        /// On success, returns length of the marker, and populates
        /// data with the details.  On failure, returns 0.
        /// </summary>
        /// <remarks>Original: int parse_list_marker(string ln, int pos, ref ListData dataptr)</remarks>
        private static int ParseListMarker(string ln, int pos, out ListData data)
        {
            char? c;
            int startpos;
            int start = 1;
            data = null;

            startpos = pos;
            c = BString.bchar(ln, pos);

            if ((c == '*' || c == '-' || c == '+') && 0 == Scanner.scan_hrule(ln, pos))
            {
                pos++;
                if (pos == ln.Length || !char.IsWhiteSpace(ln[pos]))
                    return 0;

                data = new ListData();
                data.MarkerOffset = 0; // will be adjusted later
                data.ListType = ListType.Bullet;
                data.BulletChar = c.Value;
                data.Start = 1;
                data.Delimiter = ListDelimiter.Period;
                data.IsTight = false;
            }
            else if (c != null && char.IsDigit(c.Value))
            {

                pos++;
                while (char.IsDigit(BString.bchar(ln, pos).Value))
                {
                    pos++;
                }

                if (!int.TryParse(ln.Substring(startpos, pos - startpos), 
                    System.Globalization.NumberStyles.Integer, 
                    System.Globalization.CultureInfo.InvariantCulture, out start))
                {
                    // the only reasonable explanation why this case would occur is if the number is larger than int.MaxValue.
                    return 0;
                }

                c = BString.bchar(ln, pos);
                if (c == '.' || c == ')')
                {
                    pos++;
                    if (!char.IsWhiteSpace(BString.bchar(ln, pos).Value))
                        return 0;

                    data = new ListData();
                    data.MarkerOffset = 0; // will be adjusted later
                    data.ListType = ListType.Ordered;
                    data.BulletChar = '\0';
                    data.Start = start;
                    data.Delimiter = (c == '.' ? ListDelimiter.Period : ListDelimiter.Parenthesis);
                    data.IsTight = false;
                }
                else
                {
                    return 0;
                }

            }
            else
            {
                return 0;
            }

            return (pos - startpos);
        }

        // Return 1 if list item belongs in list, else 0.
        private static bool lists_match(ListData list_data, ListData item_data)
        {
            return (list_data.ListType == item_data.ListType &&
                    list_data.Delimiter == item_data.Delimiter &&
                // list_data.marker_offset == item_data.marker_offset &&
                    list_data.BulletChar == item_data.BulletChar);
        }

        // Process one line at a time, modifying a block.
        // Returns 0 if successful.  curptr is changed to point to
        // the currently open block.
        public static int incorporate_line(string ln, int line_number, ref Block curptr)
        {
            // the original C code terminates each code with '\n'. TextReader.ReadLine() does not do so - we need to add it manually.
            ln += "\n";

            Block last_matched_container;
            int offset = 0;
            int matched = 0;
            int lev = 0;
            int i;
            ListData data;
            bool all_matched = true;
            Block container;
            Block cur = curptr;
            bool blank = false;
            int first_nonspace;
            int indent;

            // detab input line
            ln = Utilities.Untabify(ln);

            // container starts at the document root.
            container = cur.Top;

            // for each containing block, try to parse the associated line start.
            // bail out on failure:  container will point to the last matching block.

            while (container.LastChild != null && container.LastChild.IsOpen)
            {
                container = container.LastChild;

                first_nonspace = offset;
                while (BString.bchar(ln, first_nonspace) == ' ')
                    first_nonspace++;

                indent = first_nonspace - offset;
                blank = BString.bchar(ln, first_nonspace) == '\n';

                if (container.Tag == BlockTag.BlockQuote)
                {

                    matched = (indent <= 3 && BString.bchar(ln, first_nonspace) == '>') ? 1 : 0;
                    if (matched != 0)
                    {
                        offset = first_nonspace + 1;
                        if (BString.bchar(ln, offset) == ' ')
                            offset++;
                    }
                    else
                    {
                        all_matched = false;
                    }

                }
                else if (container.Tag == BlockTag.ListItem)
                {

                    if (indent >= container.Attributes.ListData.MarkerOffset +
                        container.Attributes.ListData.Padding)
                    {
                        offset += container.Attributes.ListData.MarkerOffset +
                          container.Attributes.ListData.Padding;
                    }
                    else if (blank)
                    {
                        offset = first_nonspace;
                    }
                    else
                    {
                        all_matched = false;
                    }

                }
                else if (container.Tag == BlockTag.IndentedCode)
                {

                    if (indent >= CODE_INDENT)
                    {
                        offset += CODE_INDENT;
                    }
                    else if (blank)
                    {
                        offset = first_nonspace;
                    }
                    else
                    {
                        all_matched = false;
                    }

                }
                else if (container.Tag == BlockTag.AtxHeader ||
                         container.Tag == BlockTag.SETextHeader)
                {

                    // a header can never contain more than one line
                    all_matched = false;

                }
                else if (container.Tag == BlockTag.FencedCode)
                {

                    // skip optional spaces of fence offset
                    i = container.Attributes.FencedCodeData.FenceOffset;
                    while (i > 0 && BString.bchar(ln, offset) == ' ')
                    {
                        offset++;
                        i--;
                    }

                }
                else if (container.Tag == BlockTag.HtmlBlock)
                {

                    if (blank)
                    {
                        all_matched = false;
                    }

                }
                else if (container.Tag == BlockTag.Paragraph)
                {

                    if (blank)
                    {
                        container.IsLastLineBlank = true;
                        all_matched = false;
                    }

                }

                if (!all_matched)
                {
                    container = container.Parent;  // back up to last matching block
                    break;
                }
            }

            last_matched_container = container;

            // check to see if we've hit 2nd blank line, break out of list:
            if (blank && container.IsLastLineBlank)
            {
                break_out_of_lists(ref container, line_number);
            }

            // unless last matched container is code block, try new container starts:
            while (container.Tag != BlockTag.FencedCode && container.Tag != BlockTag.IndentedCode &&
                   container.Tag != BlockTag.HtmlBlock)
            {

                first_nonspace = offset;
                while (BString.bchar(ln, first_nonspace) == ' ')
                    first_nonspace++;

                indent = first_nonspace - offset;
                blank = BString.bchar(ln, first_nonspace) == '\n';

                if (indent >= CODE_INDENT)
                {

                    if (cur.Tag != BlockTag.Paragraph && !blank)
                    {
                        offset += CODE_INDENT;
                        container = add_child(container, BlockTag.IndentedCode, line_number, offset + 1);
                    }
                    else
                    { // indent > 4 in lazy line
                        break;
                    }

                }
                else if (BString.bchar(ln, first_nonspace) == '>')
                {

                    offset = first_nonspace + 1;
                    // optional following character
                    if (BString.bchar(ln, offset) == ' ')
                    {
                        offset++;
                    }
                    container = add_child(container, BlockTag.BlockQuote, line_number, offset + 1);

                }
                else if (0 != (matched = Scanner.scan_atx_header_start(ln, first_nonspace)))
                {

                    offset = first_nonspace + matched;
                    container = add_child(container, BlockTag.AtxHeader, line_number, offset + 1);
                    int hashpos = BString.bstrchrp(ln, '#', first_nonspace);

                    if (hashpos == -1)
                        throw new CommonMarkException("ATX header parsing with regular expression returned incorrect results.", curptr);

                    int level = 0;
                    while (BString.bchar(ln, hashpos) == '#')
                    {
                        level++;
                        hashpos++;
                    }
                    container.Attributes.HeaderLevel = level;

                }
                else if (0 != (matched = Scanner.scan_open_code_fence(ln, first_nonspace)))
                {

                    container = add_child(container, BlockTag.FencedCode, line_number, first_nonspace + 1);
                    container.Attributes.FencedCodeData.FenceChar = ln[first_nonspace];
                    container.Attributes.FencedCodeData.FenceLength = matched;
                    container.Attributes.FencedCodeData.FenceOffset = first_nonspace - offset;
                    offset = first_nonspace + matched;

                }
                else if (0 != (matched = Scanner.scan_html_block_tag(ln, first_nonspace)))
                {

                    container = add_child(container, BlockTag.HtmlBlock, line_number,
                                        first_nonspace + 1);
                    // note, we don't adjust offset because the tag is part of the text

                }
                else if (container.Tag == BlockTag.Paragraph &&
                        0 != (lev = Scanner.scan_setext_header_line(ln, first_nonspace)) &&
                    // check that there is only one line in the paragraph:
                         BString.bstrrchrp(container.StringContent, '\n',
                                   container.StringContent.Length - 2) == -1)
                {

                    container.Tag = BlockTag.SETextHeader;
                    container.Attributes.HeaderLevel = lev;
                    offset = ln.Length - 1;

                }
                else if (!(container.Tag == BlockTag.Paragraph && !all_matched) &&
                         0 != (matched = Scanner.scan_hrule(ln, first_nonspace)))
                {

                    // it's only now that we know the line is not part of a setext header:
                    container = add_child(container, BlockTag.HorizontalRuler, line_number, first_nonspace + 1);
                    finalize(container, line_number);
                    container = container.Parent;
                    offset = ln.Length - 1;

                }
                else if (0 != (matched = ParseListMarker(ln, first_nonspace, out data)))
                {

                    // compute padding:
                    offset = first_nonspace + matched;
                    i = 0;
                    while (i <= 5 && BString.bchar(ln, offset + i) == ' ')
                    {
                        i++;
                    }
                    // i = number of spaces after marker, up to 5
                    if (i >= 5 || i < 1 || BString.bchar(ln, offset) == '\n')
                    {
                        data.Padding = matched + 1;
                        if (i > 0)
                        {
                            offset += 1;
                        }
                    }
                    else
                    {
                        data.Padding = matched + i;
                        offset += i;
                    }

                    // check container; if it's a list, see if this list item
                    // can continue the list; otherwise, create a list container.

                    data.MarkerOffset = indent;

                    if (container.Tag != BlockTag.List ||
                        !lists_match(container.Attributes.ListData, data))
                    {
                        container = add_child(container, BlockTag.List, line_number,
                      first_nonspace + 1);
                        container.Attributes.ListData = data;
                    }

                    // add the list item
                    container = add_child(container, BlockTag.ListItem, line_number,
                        first_nonspace + 1);
                    container.Attributes.ListData = data;
                }
                else
                {
                    break;
                }

                if (accepts_lines(container.Tag))
                {
                    // if it's a line container, it can't contain other containers
                    break;
                }
            }

            // what remains at offset is a text line.  add the text to the
            // appropriate container.

            first_nonspace = offset;
            while (BString.bchar(ln, first_nonspace) == ' ')
            {
                first_nonspace++;
            }

            indent = first_nonspace - offset;
            blank = BString.bchar(ln, first_nonspace) == '\n';

            // block quote lines are never blank as they start with >
            // and we don't count blanks in fenced code for purposes of tight/loose
            // lists or breaking out of lists.  we also don't set last_line_blank
            // on an empty list item.
            container.IsLastLineBlank = (blank &&
                                          container.Tag != BlockTag.BlockQuote &&
                                          container.Tag != BlockTag.FencedCode &&
                                          !(container.Tag == BlockTag.ListItem &&
                                            container.FirstChild == null &&
                                            container.StartLine == line_number));

            Block cont = container;
            while (cont.Parent != null)
            {
                cont.Parent.IsLastLineBlank = false;
                cont = cont.Parent;
            }

            if (cur != last_matched_container &&
                container == last_matched_container &&
                !blank &&
                cur.Tag == BlockTag.Paragraph &&
                cur.StringContent.Length > 0)
            {

                add_line(cur, ln, offset);

            }
            else
            { // not a lazy continuation

                // finalize any blocks that were not matched and set cur to container:
                while (cur != last_matched_container)
                {

                    finalize(cur, line_number);
                    cur = cur.Parent;

                    if (cur == null)
                        throw new CommonMarkException("Cannot finalize container block. Last matched container tag = " + last_matched_container.Tag);

                }

                if (container.Tag == BlockTag.IndentedCode)
                {

                    add_line(container, ln, offset);

                }
                else if (container.Tag == BlockTag.FencedCode)
                {

                    matched = (indent <= 3
                      && BString.bchar(ln, first_nonspace) == container.Attributes.FencedCodeData.FenceChar)
                      && (0 != Scanner.scan_close_code_fence(ln, first_nonspace, container.Attributes.FencedCodeData.FenceLength))
                      ? 1 : 0;
                    if (matched != 0)
                    {
                        // if closing fence, don't add line to container; instead, close it:
                        finalize(container, line_number);
                        container = container.Parent; // back up to parent
                    }
                    else
                    {
                        add_line(container, ln, offset);
                    }

                }
                else if (container.Tag == BlockTag.HtmlBlock)
                {

                    add_line(container, ln, offset);

                }
                else if (blank)
                {

                    // ??? do nothing

                }
                else if (container.Tag == BlockTag.AtxHeader)
                {

                    // chop off trailing ###s...use a scanner?
                    ln = ln.TrimEnd();
                    int p = ln.Length - 1;
                    int numhashes = 0;
                    // if string ends in #s, remove these:
                    while (p >= 0 && BString.bchar(ln, p) == '#')
                    {
                        p--;
                        numhashes++;
                    }
                    if (p >= 0 && BString.bchar(ln, p) == '\\')
                    {
                        // the last # was escaped, so we include it.
                        p++;
                        numhashes--;
                    }
                    ln = ln.Remove(p + 1, numhashes);
                    add_line(container, ln, first_nonspace);
                    finalize(container, line_number);
                    container = container.Parent;

                }
                else if (accepts_lines(container.Tag))
                {

                    add_line(container, ln, first_nonspace);

                }
                else if (container.Tag != BlockTag.HorizontalRuler && container.Tag != BlockTag.SETextHeader)
                {

                    // create paragraph container for line
                    container = add_child(container, BlockTag.Paragraph, line_number, first_nonspace + 1);
                    add_line(container, ln, first_nonspace);

                }
                else
                {

                    Utilities.Warning("Line {0} with container type {1} did not match any condition:\n\"{2}\"", line_number, container.Tag, ln);

                }

                curptr = container;
            }

            return 0;
        }
    }
}
