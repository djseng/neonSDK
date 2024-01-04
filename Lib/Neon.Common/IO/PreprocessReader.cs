//-----------------------------------------------------------------------------
// FILE:        PreprocessReader.cs
// CONTRIBUTOR: Jeff Lill
// COPYRIGHT:   Copyright © 2005-2024 by NEONFORGE LLC.  All rights reserved.
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

using System;
using System.Collections.Generic;
using System.Diagnostics.Contracts;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

using Neon.Common;
using Neon.Deployment;
using Neon.Tasks;

namespace Neon.IO
{
    /// <summary>
    /// Preprocesses text returned by a <see cref="TextReader"/> by removing comments,
    /// expanding variables, and implementing simple conditionals.
    /// </summary>
    /// <remarks>
    /// <note>
    /// This class only implements <see cref="ReadLine"/>, <see cref="ReadLineAsync"/>,
    /// <see cref="ReadToEnd"/>, and <see cref="ReadToEndAsync"/>.  The other methods
    /// will throw a <see cref="NotImplementedException"/>.
    /// </note>
    /// <para>
    /// The processor removes comment lines from the text returned.  A comment line 
    /// starts with zero or more whitespace characters followed by "<b>//</b>".
    /// </para>
    /// <note>
    /// Comment lines are indicated by a "<b>//</b>" prefix by default but this can
    /// be modified via <see cref="ClearCommentMarkers()"/> and <see cref="AddCommentMarker(string)"/>.
    /// </note>
    /// <para>
    /// The processor implements simple macro definition and conditional statements.
    /// These statements are identifying by a line with the pound sign (<b>#</b>) as
    /// the first non-whitespace character.
    /// </para>
    /// <note>
    /// The processor statement character defaults to the pound sign (<b>#</b>) but
    /// can be changed by setting <see cref="StatementMarker"/>.
    /// </note>
    /// <para>
    /// The following processing statements are supported:
    /// </para>
    /// <list type="table">
    /// <item>
    ///     <term><b>#define NAME [=VALUE]</b></term>
    ///     <description>
    ///     <para>
    ///     Defines a normal variable and setting an optional value.  The empty
    ///     string will be set by default.  These variables can be referenced
    ///     in processing statements or normal text lines as <b>$&lt;name&gt;</b>.
    ///     </para>
    ///     <para>
    ///     Variable names are case sensitive and may include letter, number, dash,
    ///     period, and underscore characters.
    ///     </para>
    ///     <para>
    ///     By default, defined variables may be referenced like <b>$&lt;name&gt;</b> and
    ///     environment variables like <b>$&lt;name&gt;</b>.
    ///     </para>
    ///     </description>
    /// </item>
    /// <item>
    ///     <term><b>#if EXPRESSION</b></term>
    ///     <description>
    ///     <para>
    ///     Conditionally includes text up to the next <b>#else</b> or
    ///     <b>#endif</b> statement.  The following expressions are
    ///     supported:
    ///     </para>
    ///     <list type="bullet">
    ///         <item><b>VALUE1 == VALUE2</b></item>
    ///         <item><b>VALUE1 != VALUE2</b></item>
    ///         <item><b>defined(NAME)</b></item>
    ///         <item><b>undefined(NAME)</b></item>
    ///     </list>
    ///     <para>
    ///     The comparisions are performed after any variables are expanded.  The
    ///     values are trimmed on bothe ends and the string comparision is case
    ///     sensitive.
    ///     </para>
    ///     </description>
    /// </item>
    /// <item>
    ///     <term><b>#else</b></term>
    ///     <description>
    ///     This can optionally be used within an <b>#if</b> statement to include lines
    ///     when the condition is false.
    ///     </description>
    /// </item>
    /// <item>
    ///     <term><b>#endif</b></term>
    ///     <description>
    ///     This terminates an <b>#if</b> statement.
    ///     </description>
    /// </item>
    /// <item>
    ///     <term><b>#switch VALUE</b></term>
    ///     <description>
    ///     Provides an easy to conditionally include statements for multiple conditions.
    ///     The subsequent  <b>#case</b> and <b>#default</b> statements up to the next 
    ///     <b>#endswitch</b> statement will be processed.
    ///     </description>
    /// </item>
    /// <item>
    ///     <term><b>#case VALUE</b></term>
    ///     <description>
    ///     This command causes the lines up to the next <b>#case</b>, <b>#default</b>,
    ///     or <b>#endswitch</b> to be outputed if the value matches that specified for
    ///     the parent <b>#switch</b> statement.
    ///     </description>
    /// </item>
    /// <item>
    ///     <term><b>#default</b></term>
    ///     <description>
    ///     This command causes the lines up to the next <b>#endswitch</b> to be outputed
    ///     if the value wasn't matched by any of the previous <b>case</b> statements.
    ///     <note>
    ///     <b>#default</b> must appear after all <b>#case</b> statements.
    ///     </note>
    ///     </description>
    /// </item>
    /// <item>
    ///     <term><b>#endswitch</b></term>
    ///     <description>
    ///     This command terminates a <b>#switch</b> statement.
    ///     </description>
    /// </item>
    /// </list>
    /// <para>
    /// Normal variables can be defined within the source text itself using the 
    /// <b>#define</b> command described above and variables may also be added 
    /// in code using <see cref="Set(string, string)"/>.  These variables can be 
    /// referenced as <b>$&lt;NAME&gt;</b>.  Environment variables can be referenced
    /// like <b>$&lt;env:NAME&gt;</b>, secrets like <b>$&lt;secret:NAME</b>, and
    /// profile values like: <b>$&lt;profile:NAME</b>.
    /// </para>
    /// <note>
    /// You may encounter situations where the default ways of referencing variables
    /// conflicts with the syntax of the underlying source text being processed.
    /// In these cases, you can set <see cref="VariableExpansionRegex"/> to 
    /// <see cref="CurlyVariableExpansionRegex"/> or <see cref="ParenVariableExpansionRegex"/>
    /// to change the format.
    /// </note>
    /// <para>
    /// Variables are always expanded in <b>#if</b> and <b>switch</b> statements and
    /// are expanded by default in the other source lines.  Variables are expanded
    /// using the <b>$&lt;NAME&gt;</b> syntax by default.  The syntax can be modified
    /// by setting <see cref="VariableExpansionRegex"/> and variable expansion can be disabled
    /// by setting <see cref="ExpandVariables"/>=<c>false</c>.
    /// </para>
    /// <note>
    /// By default, the reader will throw a <see cref="KeyNotFoundException"/> if an
    /// undefined normal variable is encountered.  This behavior can be modified by setting
    /// <see cref="DefaultVariable"/> to a non-<c>null</c> string.  In this case,
    /// undefined variable references will always be replaced by the value set.
    /// <see cref="DefaultVariable"/> defaults to <c>null</c>.
    /// </note>
    /// <note>
    /// By default, the reader will throw a <see cref="KeyNotFoundException"/> if an
    /// undefined environment variable is encountered.  This behavior can be modified by setting
    /// <see cref="DefaultEnvironmentVariable"/> to a non-<c>null</c> string.  In this case,
    /// undefined environment variable references will always be replaced by the value set.
    /// <see cref="DefaultEnvironmentVariable"/> defaults to <c>null</c>.
    /// </note>
    /// <para>
    /// Processing can also be customized via the <see cref="StripComments"/>, <see cref="RemoveComments"/>,
    /// <see cref="RemoveBlank"/>,  <see cref="ProcessStatements"/>, <see cref="Indent"/>, <see cref="TabStop"/>, 
    /// and <see cref="StatementMarker"/>
    /// properties.
    /// </para>
    /// <para><b>Secret and profile Values</b></para>
    /// <para>
    /// This class can integrate with a <see cref="IProfileClient"/> implementation added to
    /// <see cref="NeonHelper.ServiceContainer"/>.  This provides a way to abstract access to
    /// secrets and profile values from an external source.  Three item types are supported:
    /// </para>
    /// <list type="table">
    /// <item>
    ///     <term><b>secret</b></term>
    ///     <description>
    ///     <para>
    ///     Secret passwords are often protected by a password manager.  NEONFORGE has standardized
    ///     internally on 1Password for example.  Passwords are often required to satisfy complexity
    ///     and other rules.
    ///     </para>
    ///     <para>
    ///     Passwords are named by a string and are often persisted to a named location.  1Password stores
    ///     to secrets in <i>vaults</i>.  You'll need the password name and optionally, its location when
    ///     referencing a password value.
    ///     </para>
    ///     <para>
    ///     You may also request a specific secret property using an array syntax like:
    ///     </para>
    ///     <example>
    ///     SECRETNAME[PROPERTY]
    ///     </example>
    ///     <para>
    ///     Doing this overrides the default <b>password</b> property.
    ///     </para>
    ///     </description>
    /// </item>
    /// <item>
    ///     <term><b>profile-value</b></term>
    ///     <description>
    ///     <para>
    ///     Profile values are string name/value pairs that include non-secret definitions for
    ///     the user, workstation, or overall environment such as the LAN.  These can come in
    ///     handy when implementing CI/CD where each server/user can be assigned unique profile
    ///     values that reference specific test endpoints, etc.  This is quite powerful.
    ///     </para>
    ///     <para>
    ///     Profile values are simply named by a string.  There is currently no concept of a source, 
    ///     location like secrets may have.
    ///     </para>
    ///     </description>
    /// </item>
    /// </list>
    /// <para>
    /// Secrets and profile values can be referenced via <b>&lt;TYPE:NAME[:SOURCE]&gt;</b>
    /// where <b>TYPE</b> is one of <b>env</b>, <b>secret</b> (value), or <b>profile</b> and <b>NAME</b>
    /// identifies the secret or profile value and <b>source</b> optionally specifies the secret source
    /// (this is ignored for profile values).
    /// </para>
    /// <para>
    /// By default, a secret reference is replaced by the <b>password</b> property within the 
    /// named secret.  Use can use the square bracket syntax to select a different secret property.
    /// </para>
    /// <para>
    /// <b>Examples:</b>
    /// </para>
    /// <code>
    /// $&lt;secret:my-secret;my-vault&gt;      # secret from specific source
    /// $&lt;secret:my-secret&gt;               # secret password from default source
    /// $&lt;secret:my-secret[username]&gt;     # retrieve [username] from secret
    /// $&lt;secret:my-secret[password]&gt;     # retrieve [password] from secret
    /// $&lt;profile:my-profile&gt;             # profile value
    /// </code>
    /// <para>
    /// This class will throw <see cref="ProfileException"/> when it encounters a secret/profile
    /// reference and there no injected <see cref="IProfileClient"/> implementation or if the implementation
    /// has trouble communicating with the profile server.  This class also also throws a 
    /// <see cref="KeyNotFoundException"/> when a named secret or profile value doesn't exist.
    /// </para>
    /// </remarks>
    /// <threadsafety instance="false"/>
    public class PreprocessReader : TextReader
    {
        //---------------------------------------------------------------------
        // Private types

        /// <summary>
        /// Used to identify the current conditional statement.
        /// </summary>
        private enum ConditionalType
        {
            None = 0,
            If,
            Switch
        }

        /// <summary>
        /// Holds the current processing state.
        /// </summary>
        private class State
        {
            public bool             OutputEnabled;
            public ConditionalType  ConditionalType;
            public string           SwitchValue;
            public bool             SwitchHandled;
            public bool             SwitchDefaultHandled;
        }

        //---------------------------------------------------------------------
        // Static members

        private const RegexOptions      regexOptions           = RegexOptions.Compiled;
        private const RegexOptions      regexIgnoreCaseOptions = RegexOptions.IgnoreCase | RegexOptions.Compiled;
        private static readonly char[]  colonArray             = new char[] { ':' };

        /// <summary>
        /// <para>
        /// A variable expansion <see cref="Regex"/> that matches normal variables like <b>$&lt;NAME&gt;</b>, environment
        /// variables like <b>$&lt;env:NAME&gt;</b>, profile value references like <b>$&lt;profile:NAME&gt;</b> and
        /// secret references like <b>$&lt;secret:NAME&gt;</b>, <b>$&lt;secret:NAME:SOURCE&gt;</b>, <b>$&lt;secret:NAME[PROPERTY]&gt;</b>
        /// or <b>$&lt;secret:NAME[PROPERTY]:SOURCE&gt;</b>
        /// </para>
        /// <para>
        /// You can set the <see cref="VariableExpansionRegex"/> property to this value to change the
        /// <see cref="PreprocessReader"/> behavior.
        /// </para>
        /// </summary>
        public static Regex AngleVariableExpansionRegex { get; private set; } = new Regex(@"\$<(?<name>(env:|secret:|profile:)?[a-z0-9_:\.\-\[\]]+)>", regexIgnoreCaseOptions);

        /// <summary>
        /// <para>
        /// A variable expansion <see cref="Regex"/> that matches normal variables like <b>${NAME}</b>, environment
        /// variables like <b>${env:NAME}</b>, profile value references like <b>${profile:NAME}</b> and
        /// secret references like <b>${secret:NAME}</b>, <b>${secret:NAME:SOURCE}</b>, <b>${secret:NAME[PROPERTY]}</b>
        /// or <b>${secret:NAME[PROPERTY]:SOURCE}</b>
        /// </para>
        /// <para>
        /// You can set the <see cref="VariableExpansionRegex"/> property to this value to change the
        /// <see cref="PreprocessReader"/> behavior.
        /// </para>
        /// </summary>
        public static Regex CurlyVariableExpansionRegex { get; private set; } = new Regex(@"\$\{(?<name>(env:|secret:|profile:)?[a-z0-9_:\.\-\[\]]+)\}", regexIgnoreCaseOptions);

        /// <summary>
        /// <para>
        /// A variable expansion <see cref="Regex"/> that matches normal variables like <b>$(NAME)</b>, environment
        /// variables like <b>$(env:NAME)</b>, profile value references like <b>$(profile:NAME)</b> and
        /// secret references like <b>$(secret:NAME)</b>, <b>$(secret:NAME:SOURCE)</b>, <b>$(secret:NAME[PROPERTY])</b>
        /// or <b>$(secret:NAME[PROPERTY]:SOURCE)</b>
        /// </para>
        /// <para>
        /// You can set the <see cref="VariableExpansionRegex"/> property to this value to change the
        /// <see cref="PreprocessReader"/> behavior.
        /// </para>
        /// </summary>
        public static Regex ParenVariableExpansionRegex { get; private set; } = new Regex(@"\$\((?<name>(env:|secret:|profile:)?[a-z0-9_:\.\-\[\]]+)\)", regexIgnoreCaseOptions);

        /// <summary>
        /// The default variable expansion <see cref="Regex"/> that matches normal variables like <b>$&lt;test&gt;</b>, environment
        /// variables like <b>&lt;env:test&gt;</b>, profile references like <b>&lt;profile:my-profile&gt;</b>, and secret
        /// references like <b>&lt;secret:NAME:SOURCE&gt;</b>.  You can set the <see cref="VariableExpansionRegex"/>
        /// property to this value to change the <see cref="PreprocessReader"/> behavior.
        /// </summary>
        public static Regex DefaultVariableExpansionRegex { get; private set; } = AngleVariableExpansionRegex;

        /// <summary>
        /// <b>INTERNAL USE ONLY:</b> The <see cref="Regex"/> used to validate variable names.
        /// </summary>
        public static Regex VariableValidationRegex { get; private set; } = new Regex(@"^[a-z0-9_\.\-]+$", regexIgnoreCaseOptions);

        //---------------------------------------------------------------------
        // Instance members

        private TextReader                  reader;
        private IProfileClient              profileClient          = NeonHelper.ServiceContainer.GetService<IProfileClient>();
        private Dictionary<string, string>  variables              = new Dictionary<string, string>();
        private Regex                       variableExpansionRegex = DefaultVariableExpansionRegex;
        private List<string>                commentMarkers         = new List<string>() { "//" };
        private string                      indent                 = string.Empty;
        private int                         tabStop                = 0;
        private Stack<State>                stateStack;
        private int                         lineNumber;

        /// <summary>
        /// Constructs an over another <see cref="TextReader"/>.
        /// </summary>
        /// <param name="reader">The source <see cref="TextReader"/>.</param>
        public PreprocessReader(TextReader reader)
        {
            Covenant.Requires<ArgumentNullException>(reader != null, nameof(reader));

            this.reader            = reader;
            this.stateStack        = new Stack<State>();
            this.lineNumber        = 0;

            this.stateStack.Push(new State() { OutputEnabled = true });
        }

        /// <summary>
        /// Constructs an instance over another <see cref="TextReader"/>, initializing some variables.
        /// </summary>
        /// <param name="reader">The source <see cref="TextReader"/>.</param>
        /// <param name="variables">The variables.</param>
        public PreprocessReader(TextReader reader, Dictionary<string, string> variables)
            : this(reader)
        {
            Covenant.Requires<ArgumentNullException>(reader != null, nameof(reader));

            if (variables != null)
            {
                foreach (var item in variables)
                {
                    Set(item.Key, item.Value);
                }
            }
        }

        /// <summary>
        /// Constructs an instance from a string.
        /// </summary>
        /// <param name="input">The input string.</param>
        public PreprocessReader(string input)
            : this(new StringReader(input))
        {
        }

        /// <summary>
        /// Constructs an instance from a string, initializing some variables.
        /// </summary>
        /// <param name="input">The input string.</param>
        /// <param name="variables">The variables.</param>
        public PreprocessReader(string input, Dictionary<string, string> variables)
            : this(new StringReader(input), variables)
        {
        }

        /// <summary>
        /// Constructs an instance from UTF-8 encoded bytes.
        /// </summary>
        /// <param name="bytes">The input data.</param>
        public PreprocessReader(byte[] bytes)
            : this(new StringReader(Encoding.UTF8.GetString(bytes)))
        {
        }

        /// <summary>
        /// Constructs an instance from  UTF-8 encoded bytes, initializing some variables.
        /// </summary>
        /// <param name="bytes">The input data.</param>
        /// <param name="variables">The variables.</param>
        public PreprocessReader(byte[] bytes, Dictionary<string, string> variables)
            : this(new StringReader(Encoding.UTF8.GetString(bytes)), variables)
        {
        }

        /// <summary>
        /// The leading character used to identify a preprocessing statement.
        /// This defaults to the pound sign (<b>#</b>).
        /// </summary>
        public char StatementMarker { get; set; } = '#';

        /// <summary>
        /// Clears any comment markers, effectively disabling comment stripping.
        /// </summary>
        public void ClearCommentMarkers()
        {
            commentMarkers.Clear();
        }

        /// <summary>
        /// Appends a comment marker prefix.  This must be a non-empty string including only
        /// non-whitespace punctuation characters.
        /// </summary>
        /// <param name="marker">The comment prefix.</param>
        public void AddCommentMarker(string marker)
        {
            Covenant.Requires<ArgumentNullException>(!string.IsNullOrEmpty(marker), nameof(marker));
            Covenant.Requires<ArgumentException>(!marker.Any(ch => char.IsWhiteSpace(ch)), () => nameof(marker), () => $"[marker={marker}]: cannot include whitespace.");
            Covenant.Requires<ArgumentException>(!marker.Any(ch => !char.IsPunctuation(ch)), () => nameof(marker), () => $"[marker={marker}]: includes a non-punctuation character.");

            // Add the marker if it's not already present.

            if (!commentMarkers.Exists(m => m == marker))
            {
                commentMarkers.Add(marker);
            }
        }

        /// <summary>
        /// Configures the reader for parsing YAML by setting the <see cref="StatementMarker"/> to <b>"@"</b>
        /// and the comment marker to <b>"#"</b>.
        /// </summary>
        public void SetYamlMode()
        {
            StatementMarker = '@';
            
            ClearCommentMarkers();
            AddCommentMarker("#");
        }

        /// <summary>
        /// The <see cref="Regex"/> used to match variable expansions.  This defaults
        /// to matching variables of the form: <b>${NAME}</b>.
        /// </summary>
        /// <remarks>
        /// <note>
        /// You may use set encounter situations where the default syntax would
        /// conflict with the source text being processed.  You map use the
        /// <see cref="CurlyVariableExpansionRegex"/> or <see cref="ParenVariableExpansionRegex"/>
        /// patterns as an alternative.
        /// </note>
        /// </remarks>
        public Regex VariableExpansionRegex
        {
            get { return variableExpansionRegex; }

            set
            {
                Covenant.Requires<ArgumentNullException>(value != null, nameof(VariableExpansionRegex));

                variableExpansionRegex = value;
            }
        }

        /// <summary>
        /// Controls whether variables in the source are expanded.  This defaults
        /// to <c>true</c>.
        /// </summary>
        public bool ExpandVariables { get; set; } = true;

        /// <summary>
        /// The default value to use for an undefined normal variable or <c>null</c>
        /// if a <see cref="KeyNotFoundException"/> is to be thrown when a
        /// undefined non-environment variable is referenced.  This defaults to 
        /// <c>null</c>.
        /// </summary>
        public string DefaultVariable { get; set; } = null;

        /// <summary>
        /// The default value to use for an undefined environment variable or <c>null</c>
        /// if a <see cref="KeyNotFoundException"/> is to be thrown when a
        /// undefined environment variable is referenced.  This defaults to 
        /// <c>null</c>.
        /// </summary>
        public string DefaultEnvironmentVariable { get; set; } = null;

        /// <summary>
        /// Controls whether comments are stripped out while reading.  This defaults
        /// to <c>true</c>.
        /// </summary>
        /// <remarks>
        /// <note>
        /// <see cref="StripComments"/> returns a blank line for comments and
        /// <see cref="RemoveComments"/> doesn't return a comment line at all.
        /// </note>
        /// </remarks>
        public bool StripComments { get; set; } = true;

        /// <summary>
        /// Controls whether comments are removed while reading.  This defaults
        /// to <c>false</c>.
        /// </summary>
        /// <remarks>
        /// <note>
        /// <see cref="StripComments"/> returns a blank line for comments and
        /// <see cref="RemoveComments"/> doesn't return a comment line at all.
        /// </note>
        /// </remarks>
        public bool RemoveComments { get; set; } = false;

        /// <summary>
        /// Controls whether blank lines or lines with only whitespace are to
        /// be removed while reading.  This defaults to <c>false</c>.
        /// </summary>
        public bool RemoveBlank { get; set; } = false;

        /// <summary>
        /// Controls whether preprocessor statements are processed.  This defaults to <c>true</c>.
        /// </summary>
        public bool ProcessStatements { get; set; } = true;

        /// <summary>
        /// Controls whether embedded TAB <b>(\t)</b> characters will be converted into
        /// spaces to format tab stops correctly.  This defaults to <b>zero</b> which
        /// will not process any tabs.
        /// </summary>
        public int TabStop
        {
            get { return tabStop; }

            set
            {
                Covenant.Requires<ArgumentException>(value >= 0, nameof(TabStop));

                tabStop = value;
            }
        }

        /// <summary>
        /// The number of spaces to indent the output.  This defaults to <b>0</b>.
        /// </summary>
        public int Indent
        {
            get { return indent.Length; }

            set
            {
                Covenant.Requires<ArgumentException>(value >= 0, nameof(Indent));

                indent = new string(' ', value);
            }
        }

        /// <summary>
        /// Determines the line ending <see cref="ReadToEnd"/> and <see cref="ReadToEndAsync"/>
        /// will append to the lines they read.  This defaults to <see cref="LineEnding.Platform"/>
        /// but may be changed to <see cref="LineEnding.CRLF"/> or <see cref="LineEnding.LF"/>.
        /// </summary>
        public LineEnding LineEnding { get; set; } = LineEnding.Platform;

        /// <summary>
        /// Sets a variable to a string value.
        /// </summary>
        /// <param name="name">The case sensitive variable name.</param>
        /// <param name="value">The option value (defaults to the empty string).</param>
        public void Set(string name, string value = "")
        {
            Covenant.Requires<ArgumentNullException>(!string.IsNullOrWhiteSpace(name), nameof(name));
            Covenant.Requires<ArgumentException>(VariableValidationRegex.IsMatch(name), nameof(name));

            value = value ?? string.Empty;

            variables[name] =  value;
        }

        /// <summary>
        /// Sets a variable to an object value.
        /// </summary>
        /// <param name="name">The case sensitive variable name.</param>
        /// <param name="value">The option value (defaults to the null).</param>
        public void Set(string name, object value = null)
        {
            Covenant.Requires<ArgumentNullException>(!string.IsNullOrWhiteSpace(name), nameof(name));
            Covenant.Requires<ArgumentException>(VariableValidationRegex.IsMatch(name), nameof(name));

            value = value ?? string.Empty;

            variables[name] = value.ToString();
        }

        /// <summary>
        /// Sets a variable to an boolean value.
        /// </summary>
        /// <param name="name">The case sensitive variable name.</param>
        /// <param name="value">The option value.</param>
        /// <remarks>
        /// <note>
        /// The value set will be lowercase <b>true</b> or <b>false</b>.
        /// </note>
        /// </remarks>
        public void Set(string name, bool value)
        {
            Covenant.Requires<ArgumentNullException>(!string.IsNullOrWhiteSpace(name), nameof(name));
            Covenant.Requires<ArgumentException>(VariableValidationRegex.IsMatch(name), nameof(name));

            variables[name] = NeonHelper.ToBoolString(value);
        }

        /// <inheritdoc/>
        public override string ReadLine()
        {
            string  line;

            while (true)
            {
                line = reader.ReadLine();
                lineNumber++;

                if (line == null)
                {
                    VerifyStatementClosure();
                    return null;
                }

                if (IsComment(line))
                {
                    if (RemoveComments)
                    {
                        continue;
                    }

                    if (StripComments && RemoveBlank)
                    {
                        continue;
                    }

                    return StripComments ? string.Empty : line;
                }

                if (RemoveBlank && string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                if (IsStatement(line))
                {
                    if (ProcessStatements)
                    {
                        ProcessStatement(line);
                        return string.Empty;
                    }
                    else
                    {
                        return line;
                    }
                }

                if (stateStack.Peek().OutputEnabled)
                {
                    return Expand(line);
                }
                else
                {
                    return string.Empty;
                }
            }
        }

        /// <inheritdoc/>
        public override async Task<string> ReadLineAsync()
        {
            await SyncContext.Clear;

            string line;

            while (true)
            {
                line = await reader.ReadLineAsync();
                lineNumber++;

                if (line == null)
                {
                    VerifyStatementClosure();
                    return null;
                }

                if (IsComment(line))
                {
                    if (RemoveComments)
                    {
                        continue;
                    }

                    if (StripComments && RemoveBlank)
                    {
                        continue;
                    }

                    return StripComments ? string.Empty : line;
                }

                if (RemoveBlank && string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                if (IsStatement(line))
                {
                    if (ProcessStatements)
                    {
                        ProcessStatement(line);
                        return string.Empty;
                    }
                    else
                    {
                        return line;
                    }
                }

                if (stateStack.Peek().OutputEnabled)
                {
                    return Expand(line);
                }
                else
                {
                    return string.Empty;
                }
            }
        }

        /// <inheritdoc/>
        public override string ReadToEnd()
        {
            var sb = new StringBuilder(1024);

            for (var line = ReadLine(); line != null; line = ReadLine())
            {
                switch (LineEnding)
                {
                    case LineEnding.Platform:

                        sb.AppendLine(line);
                        break;

                    case LineEnding.CRLF:

                        sb.Append(line);
                        sb.Append("\r\n");
                        break;

                    case LineEnding.LF:

                        sb.Append(line);
                        sb.Append("\n");
                        break;
                }
            }

            return sb.ToString();
        }

        /// <inheritdoc/>
        public override async Task<string> ReadToEndAsync()
        {
            await SyncContext.Clear;

            var sb = new StringBuilder(1024);

            for (var line = await ReadLineAsync(); line != null; line = await ReadLineAsync())
            {
                switch (LineEnding)
                {
                    case LineEnding.Platform:

                        sb.AppendLine(line);
                        break;

                    case LineEnding.CRLF:

                        sb.Append(line);
                        sb.Append("\r\n");
                        break;

                    case LineEnding.LF:

                        sb.Append(line);
                        sb.Append("\n");
                        break;
                }
            }

            return sb.ToString();
        }

        /// <summary>
        /// Determines whether the line passed is a comment.
        /// </summary>
        /// <param name="line">The text line.</param>
        /// <returns><b>true</b> if the line is a comment.</returns>
        private bool IsComment(string line)
        {
            line = line.TrimStart();

            foreach (var marker in commentMarkers)
            {
                if (line.StartsWith(marker))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Expands any variables and TABs in the string passed.
        /// </summary>
        /// <param name="input">The input text.</param>
        /// <returns>The expanded result.</returns>
        private string Expand(string input)
        {
            if (!ExpandVariables)
            {
                return input;
            }

            var output     = input;
            var matchCount = 0;

            while (true)
            {
                var match = VariableExpansionRegex.Match(output);

                if (!match.Success)
                {
                    break;
                }

                var group = match.Groups["name"];

                if (!group.Success)
                {
                    throw new FormatException($"Line {lineNumber}: The [{nameof(VariableExpansionRegex)}] expression does not define the [name] pattern group: {VariableExpansionRegex} ");
                }

                var name = group.Value;

                if (matchCount++ > 128)
                {
                    throw new FormatException($"Line {lineNumber}: More than 128 variable expansions are required.  Verify that there are no recursively defined variables on line: {input}");
                }

                string value;

                if (name.StartsWith("profile:") || name.StartsWith("secret:"))
                {
                    // This is a profile or secret reference.

                    var fields = name.Split(colonArray, StringSplitOptions.RemoveEmptyEntries);

                    if (fields.Length < 2)
                    {
                        throw new ProfileException($"[{name}] is not a valid profile reference.  Both [type] and [name] are required.", ProfileStatus.BadReference);
                    }

                    switch (fields[0].ToLower())
                    {
                        case "secret":

                            if (profileClient == null)
                            {
                                throw new ProfileException($"Cannot lookup the secret value [{name}] because no [{nameof(IProfileClient)}] implementation is available.", ProfileStatus.BadReference);
                            }

                            value = profileClient.GetSecretValue(fields[1], fields.Length >= 3 ? fields[2] : null);
                            break;

                        case "profile":

                            if (profileClient == null)
                            {
                                throw new ProfileException($"Cannot lookup the profile value [{name}] because no [{nameof(IProfileClient)}] implementation is available.", ProfileStatus.BadReference);
                            }

                            value = profileClient.GetProfileValue(fields[1]);
                            break;

                        default:

                            throw new ProfileException($"[{fields[0]}] is not a valid profile type.  Only [secret], and [profile] are supported.", ProfileStatus.BadReference);
                    }
                }
                else if (name.StartsWith("env:"))
                {
                    // This is an environment variable.

                    name  = name.Substring("env:".Length);
                    value = Environment.GetEnvironmentVariable(name);

                    if (value == null)
                    {
                        if (DefaultEnvironmentVariable == null)
                        {
                            throw new KeyNotFoundException($"Line {lineNumber}: Undefined environment variable reference [{match.Value}].");
                        }

                        value = DefaultEnvironmentVariable;
                    }
                }
                else
                {
                    // This is a normal variable.

                    if (!variables.TryGetValue(name, out value))
                    {
                        if (DefaultVariable == null)
                        {
                            throw new KeyNotFoundException($"Line {lineNumber}: Undefined variable reference [{match.Value}].");
                        }

                        value = DefaultVariable;
                    }
                }

                output = output.Substring(0, match.Index) + value + output.Substring(match.Index + match.Length);
            }

            if (TabStop == 0)
            {
                return IndentLine(output);
            }
            else
            {
                return IndentLine(NeonHelper.ExpandTabs(output, TabStop));
            }
        }

        /// <summary>
        /// Adds indentation to the input.
        /// </summary>
        /// <param name="input">The inpur string.</param>
        /// <returns>The indented input.</returns>
        private string IndentLine(string input)
        {
            // We don't indent empty or strings with only whitespace.

            if (indent.Length == 0 || string.IsNullOrWhiteSpace(input))
            {
                return input;
            }

            return indent + input;
        }

        /// <summary>
        /// Determines whether the line passed is a statement.
        /// </summary>
        /// <param name="line">The text line.</param>
        /// <returns><b>true</b> if the line is a statement.</returns>
        private bool IsStatement(string line)
        {
            if (line == null)
            {
                return false;
            }

            int i;

            if (line.Length < 1)
            {
                return false;
            }

            for (i = 0; i < line.Length; i++)
            {
                if (!char.IsWhiteSpace(line[i]))
                {
                    break;
                }
            }

            if (i >= line.Length - 1 || line[i] != StatementMarker)
            {
                return false;
            }

            return true;
        }

        private static Regex defineRegex = new Regex(@"^define\s+(?<name>[a-zA-Z0-9_\.\-]+)\s*(=\s*(?<value>.*)|())$", regexOptions);

        private static Regex ifRegex = new Regex(
@"^if\s
(
    ((?<value1>.*)(?<operator>(==)|(!=))(?<value2>.*))
    |
    ((?<operator>(defined)|(undefined))\((?<name>[a-z-A-Z0-9_\.\-]+)\))
)
$", regexOptions | RegexOptions.IgnorePatternWhitespace);

        private static Regex switchRegex = new Regex(@"^switch\s(?<value>.+)$", regexOptions);
        private static Regex caseRegex   = new Regex(@"^case\s(?<value>.+)$", regexOptions);

        /// <summary>
        /// Processes a statement line.
        /// </summary>
        /// <param name="line">The statement.</param>
        /// <exception cref="FormatException">Thrown for malformed statements.</exception>
        private void ProcessStatement(string line)
        {
            var pos       = line.IndexOf(StatementMarker);
            var statement = line.Substring(pos + 1).TrimEnd();

            if (statement.StartsWith("define"))
            {
                // Note: 
                //
                // We don't expand variables now for [#define] because we want to
                // be able to dereference variables dynamically.      

                var match = defineRegex.Match(statement);

                if (!match.Success)
                {
                    throw new FormatException($"Line {lineNumber}: Invalid [#define] statement: {line}");
                }

                var name  = match.Groups["name"];
                var value = match.Groups["value"];

                if (!name.Success)
                {
                    throw new FormatException($"Line {lineNumber}: Invalid [#define] statement: {line}");
                }

                if (stateStack.Peek().OutputEnabled)
                {
                    if (value.Success)
                    {
                        variables[name.Value] = value.Value.Trim();
                    }
                    else
                    {
                        variables[name.Value] = string.Empty;
                    }
                }
            }
            else if (statement.StartsWith("if"))
            {
                statement = Expand(statement);

                var match     = ifRegex.Match(statement);
                var condition = false;

                if (!match.Success)
                {
                    throw new FormatException($"Line {lineNumber}: Invalid [#if] statement: {line}");
                }

                var op     = match.Groups["operator"];
                var value1 = match.Groups["value1"];
                var value2 = match.Groups["value2"];
                var name   = match.Groups["name"];

                if (!op.Success)
                {
                    throw new FormatException($"Line {lineNumber}: Invalid [#if] statement: {line}");
                }

                switch (op.Value)
                {
                    case "==":

                        if (!value1.Success || !value2.Success)
                        {
                            throw new FormatException($"Line {lineNumber}: Invalid [#if] statement: {line}");
                        }

                        condition = value1.Value.Trim() == value2.Value.Trim();
                        break;

                    case "!=":

                        if (!value1.Success || !value2.Success)
                        {
                            throw new FormatException($"Line {lineNumber}: Invalid [#if] statement: {line}");
                        }

                        condition = value1.Value.Trim() != value2.Value.Trim();
                        break;

                    case "defined":

                        if (!name.Success)
                        {
                            throw new FormatException($"Line {lineNumber}: Invalid [#if] statement: {line}");
                        }

                        condition = variables.ContainsKey(name.Value);
                        break;

                    case "undefined":

                        if (!name.Success)
                        {
                            throw new FormatException($"Line {lineNumber}: Invalid [#if] statement: {line}");
                        }

                        condition = !variables.ContainsKey(name.Value);
                        break;

                    default:

                        throw new FormatException($"Line {lineNumber}: Invalid [#if] statement: {line}");
                }

                stateStack.Push(
                    new State()
                    {
                        OutputEnabled   = stateStack.Peek().OutputEnabled && condition,
                        ConditionalType = ConditionalType.If
                    });
            }
            else if (statement.StartsWith("else"))
            {
                var state = stateStack.Peek();

                if (state.ConditionalType != ConditionalType.If)
                {
                    throw new FormatException($"Line {lineNumber}: [#else] statement is not within an [#if]: {line}");
                }

                state.OutputEnabled = !state.OutputEnabled;
            }
            else if (statement.StartsWith("endif"))
            {
                if (stateStack.Peek().ConditionalType != ConditionalType.If)
                {
                    throw new FormatException($"Line {lineNumber}: [#endif] statement has no matching [#if]: {line}");
                }

                stateStack.Pop();
            }
            else if (statement.StartsWith("switch"))
            {
                statement = Expand(statement);

                var match = switchRegex.Match(statement);

                if (!match.Success)
                {
                    throw new FormatException($"Line {lineNumber}: Invalid [#switch] statement: {line}");
                }

                stateStack.Push(
                    new State()
                    {
                        OutputEnabled        = false,
                        ConditionalType      = ConditionalType.Switch,
                        SwitchHandled        = false,
                        SwitchDefaultHandled = false,
                        SwitchValue          = match.Groups["value"].Value.Trim(),
                    });
            }
            else if (statement.StartsWith("case"))
            {
                statement = Expand(statement);

                var state = stateStack.Peek();

                if (state.ConditionalType != ConditionalType.Switch)
                {
                    throw new FormatException($"Line {lineNumber}: Invalid [#case] statement is not within a [#switch] block: {line}");
                }

                if (state.SwitchDefaultHandled)
                {
                    throw new FormatException($"Line {lineNumber}: [#case] statement cannot appear after [#default] in a [#switch] block: {line}");
                }

                var match = caseRegex.Match(statement);

                if (!match.Success)
                {
                    throw new FormatException($"Line {lineNumber}: Invalid [#case] statement: {line}");
                }

                var value = match.Groups["value"].Value.Trim();

                if (value == state.SwitchValue)
                {
                    if (state.SwitchHandled)
                    {
                        throw new FormatException($"Line {lineNumber}: [#case] statement cannot be repeated in a [#switch] block: {line}");
                    }

                    stateStack.Pop();
                    state.OutputEnabled = true;
                    state.SwitchHandled = true;
                    stateStack.Push(state);
                }
                else
                {
                    stateStack.Pop();
                    state.OutputEnabled = false;
                    stateStack.Push(state);
                }
            }
            else if (statement.TrimEnd() == "default")
            {
                var state = stateStack.Peek();

                if (state.ConditionalType != ConditionalType.Switch)
                {
                    throw new FormatException($"Line {lineNumber}: [#case] statement is not within a [#switch] block: {line}");
                }

                if (state.SwitchHandled)
                {
                    stateStack.Pop();
                    state.OutputEnabled = false;
                    stateStack.Push(state);
                }
                else
                {
                    stateStack.Pop();
                    state.OutputEnabled        = true;
                    state.SwitchDefaultHandled = true;
                    stateStack.Push(state);
                }
            }
            else if (statement.TrimEnd() == "endswitch")
            {
                if (stateStack.Peek().ConditionalType != ConditionalType.Switch)
                {
                    throw new FormatException($"Line {lineNumber}: [#endswitch] statement has no matching [#switch]: {line}");
                }

                stateStack.Pop();
            }
            else
            {
                throw new FormatException($"Line {lineNumber}: Unknown preprocessing statement: {line}");
            }
        }

        /// <summary>
        /// Verifies that all statements have been closed properly.
        /// </summary>
        /// <exception cref="FormatException">Thrown if an [#if] or {#switch] statement has not been closed.</exception>
        private void VerifyStatementClosure()
        {
            switch (stateStack.Peek().ConditionalType)
            {
                case ConditionalType.If:

                    throw new FormatException("Unclosed [#if] statement.");

                case ConditionalType.Switch:

                    throw new FormatException("Unclosed [#switch] statement.");
            }
        }

        //---------------------------------------------------------------------
        // These methods are not implemented.

        /// <inheritdoc/>
        public override int Peek()
        {
            throw new NotImplementedException();
        }

        /// <inheritdoc/>
        public override int Read()
        {
            throw new NotImplementedException();
        }

        /// <inheritdoc/>
        public override int Read(char[] buffer, int index, int count)
        {
            throw new NotImplementedException();
        }

        /// <inheritdoc/>
        public override Task<int> ReadAsync(char[] buffer, int index, int count)
        {
            throw new NotImplementedException();
        }

        /// <inheritdoc/>
        public override int ReadBlock(char[] buffer, int index, int count)
        {
            throw new NotImplementedException();
        }

        /// <inheritdoc/>
        public override Task<int> ReadBlockAsync(char[] buffer, int index, int count)
        {
            throw new NotImplementedException();
        }
    }
}
