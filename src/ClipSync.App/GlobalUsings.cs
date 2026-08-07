// 显式消解 WPF / WinForms / System.Drawing 之间的同名类型歧义
// WinForms 类型通过各文件顶部的 `using WinForms = System.Windows.Forms;` 别名访问
// System.Drawing 类型通过 `using Drawing = System.Drawing;` 别名访问
global using Application = System.Windows.Application;
global using Button = System.Windows.Controls.Button;
global using Panel = System.Windows.Controls.Panel;
global using TextBox = System.Windows.Controls.TextBox;
global using CheckBox = System.Windows.Controls.CheckBox;
global using Image = System.Windows.Controls.Image;
global using Color = System.Windows.Media.Color;
