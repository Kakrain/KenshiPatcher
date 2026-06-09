using KenshiCore.ReverseEngineering;
using KenshiCore.Utilities;
using KenshiPatcher.ExpressionReader;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Security.Policy;
using System.Text;
using System.Threading.Tasks;
using static System.Windows.Forms.Design.AxImporter;

namespace KenshiPatcher.Forms
{
    public abstract class OptionDefinition
    {
        protected Literal<object> variable;
        public string question = "";
        protected object[]? options = null;
        protected Control? control = null;
        protected abstract void CreateControl();
        public abstract void Read();
        public OptionDefinition(Literal<object> v, string q, object[]? o)
        {
            variable = v;
            question = q;
            options = o;
        }
        public Panel Build()
        {
            CreateControl();

            var layout = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink
            };

            var label = new Label
            {
                Text = question,
                AutoSize = true
            };

            layout.Controls.Add(label);
            layout.Controls.Add(control!);

            return new Panel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Controls = { layout }
            };
        }
    }
    public class StringOptionDefinition : OptionDefinition
    {
        public StringOptionDefinition(Literal<object> v,string q,object[]? o): base(v, q, o){ }
        protected override void CreateControl()
        {
            control = new TextBox { Text = variable.Evaluate(null)?.ToString() ?? "" };
        }
        public override void Read()
        {
            variable.setValue(((TextBox)control!).Text);
        }
    }
    public class IntegerOptionDefinition : OptionDefinition
    {
        public IntegerOptionDefinition(Literal<object> v, string q, object[]? o) : base(v, q, o) { }
        protected override void CreateControl()
        {
            control = new NumericUpDown
            {
                Value = Convert.ToDecimal(variable.Evaluate(null) ?? 0),Width = 120,
                Minimum = int.MinValue,
                Maximum = int.MaxValue
            };
        }
        public override void Read()
        {
            variable.setValue((Int64)((NumericUpDown)control!).Value);
        }
    }
    public class FloatOptionDefinition : OptionDefinition
    {
        public FloatOptionDefinition(Literal<object> v, string q, object[]? o)
            : base(v, q, o) { }

        protected override void CreateControl()
        {
            control = new NumericUpDown
            {
                Value = Convert.ToDecimal(variable.Evaluate(null) ?? 0f),
                DecimalPlaces = 5,
                Increment = 0.1m,
                Width = 120
            };

            control.KeyPress += (s, e) =>
            {
                if (e.KeyChar == '.')
                {
                    e.Handled = true;
                    SendKeys.Send(",");
                }
            };

        }

        public override void Read()
        {
            variable.setValue((Double)((NumericUpDown)control!).Value);
        }
    }
    public class BoolOptionDefinition : OptionDefinition
    {
        public BoolOptionDefinition(Literal<object> v, string q, object[]? o) : base(v, q, o) { }
        protected override void CreateControl()
        {
            control = new CheckBox
            {
                Checked = Convert.ToBoolean(variable.Evaluate(null) ?? false)
            };
        }
        public override void Read()
        {
            variable.setValue(((CheckBox)control!).Checked);
        }
    }
    public class KPatcherConfigForm : Form
    {
        private FlowLayoutPanel panel;
        private Dictionary<string, Control> controls = new();
        private static KPatcherConfigForm? _instance;
        private readonly List<OptionDefinition> _options = new();
        public static KPatcherConfigForm Instance
        {
            get
            {
                if (_instance == null) _instance = new KPatcherConfigForm();
                return _instance;


            }
        }

        private KPatcherConfigForm()
        {
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            Padding = new Padding(2);

            var root = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Dock = DockStyle.Fill,
                Padding = new Padding(2)
            };

            panel = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink
            };

            var okButton = new Button
            {
                Text = "OK",
                DialogResult = DialogResult.OK,
                AutoSize = true
            };

            var cancelButton = new Button
            {
                Text = "Cancel",
                DialogResult = DialogResult.Cancel,
                AutoSize = true
            };

            AcceptButton = okButton;
            CancelButton = cancelButton;

            var buttonsPanel = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.RightToLeft,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Padding = new Padding(2)
            };

            buttonsPanel.Controls.Add(cancelButton);
            buttonsPanel.Controls.Add(okButton);

            root.Controls.Add(panel);
            root.Controls.Add(buttonsPanel);

            Controls.Add(root);
        }
        public void AddOption(Literal<object> variable, string question, object[]? options = null)
        {
            CoreUtils.Print($"Adding option for variable: {variable}");
            CoreUtils.Print($"Variable type: {variable.Evaluate(null)?.GetType()}");
            OptionDefinition? definition = variable.Evaluate(null)!.GetType() switch
            {
                Type t when t == typeof(String) => new StringOptionDefinition(variable, question, options),
                Type t when t == typeof(Int64) => new IntegerOptionDefinition(variable, question, options),
                //Type t when t == typeof(Int32) => new IntegerOptionDefinition(variable, question, options),
                Type t when t == typeof(Double) => new FloatOptionDefinition(variable, question, options),
                Type t when t == typeof(Boolean) => new BoolOptionDefinition(variable, question, options),
                _ => throw new NotSupportedException()
            };
            _options.Add(definition);
        }
        private void BuildUI()
        {
            panel.Controls.Clear();
            controls.Clear();

            foreach (var option in _options)
            {
                panel.Controls.Add(option.Build());
            }
        }
        /*public new void Show()
        {
            BuildUI();
            this.BringToFront();
            this.Activate();
            var result = ShowDialog();
            if (result == DialogResult.OK)
            {
                foreach (var option in _options)
                {
                    option.Read();
                }
            }
            else
            {
                Patcher.Instance.Stop("User cancelled configuration of patch");
            }
            _options.Clear();
        }*/

        public new void Show()
        {
            if (InvokeRequired)
            {
                Invoke(new Action(() => Show()));
                return;
            }

            BuildUI();
            BringToFront();
            Activate();

            var result = ShowDialog();

            if (result == DialogResult.OK)
            {
                foreach (var option in _options)
                    option.Read();
            }
            else
            {
                Patcher.Instance.Stop("User cancelled configuration of patch");
            }

            _options.Clear();
        }
    }
}
