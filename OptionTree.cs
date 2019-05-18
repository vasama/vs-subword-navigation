using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace SubwordNavigation
{
	class RadioButtonConverter : IValueConverter
	{
		public object Convert(object value, Type targetType,
			object parameter, System.Globalization.CultureInfo culture)
		{
			return value.Equals(parameter);
		}

		public object ConvertBack(object value, Type targetType,
			object parameter, System.Globalization.CultureInfo culture)
		{
			return (bool)value ? parameter : DependencyProperty.UnsetValue;
		}

		public static readonly RadioButtonConverter Instance =
			new RadioButtonConverter();
	}

	class OptionTree : TreeView
	{
		public OptionTree()
		{
			DataContextChanged += HandleDataContextChanged;
		}
		
		void HandleDataContextChanged(object sender,
			DependencyPropertyChangedEventArgs e)
		{
			Items.Clear();

			object options = e.NewValue;
			Type optionsType = options.GetType();

			var categories = new Dictionary<string, TreeViewItem>();

			foreach (var property in optionsType.GetProperties(
				BindingFlags.Public | BindingFlags.Instance))
			{
				Control control = null;

				Type propertyType = property.PropertyType;
				string propertyName = GetName(property);

				if (propertyType == typeof(bool))
				{
					CheckBox checkBox = new CheckBox();
					checkBox.Content = propertyName;
					checkBox.Focusable = false;

					var binding = new Binding(property.Name);
					binding.Source = options;
					binding.Mode = BindingMode.TwoWay;

					checkBox.SetBinding(CheckBox.IsCheckedProperty, binding);

					control = checkBox;
				}
				else if (propertyType.IsEnum)
				{
					TreeViewItem enumItemsControl = new TreeViewItem();
					enumItemsControl.Header = propertyName;
					enumItemsControl.Focusable = false;
					enumItemsControl.IsExpanded = true;

					foreach (var enumerator in propertyType
						.GetFields(BindingFlags.Public | BindingFlags.Static))
					{
						var radioButton = new RadioButton();
						radioButton.Content = GetName(enumerator);
						radioButton.Focusable = false;

						var binding = new Binding(property.Name);
						binding.Source = options;
						binding.Converter = RadioButtonConverter.Instance;
						binding.ConverterParameter = enumerator.GetValue(null);
						binding.Mode = BindingMode.TwoWay;

						radioButton.SetBinding(RadioButton.IsCheckedProperty, binding);

						enumItemsControl.Items.Add(radioButton);
					}

					control = enumItemsControl;
				}
				else continue;

				var categoryAttribute = property
					.GetCustomAttribute<CategoryAttribute>();

				if (categoryAttribute != null)
				{
					string category = categoryAttribute.Category;

					TreeViewItem itemsControl;
					if (!categories.TryGetValue(category, out itemsControl))
					{
						itemsControl = new TreeViewItem();
						itemsControl.Header = category;
						itemsControl.Focusable = false;
						itemsControl.IsExpanded = true;

						categories.Add(category, itemsControl);
						Items.Add(itemsControl);
					}
					itemsControl.Items.Add(control);
				}
				else
				{
					Items.Add(control);
				}
			}
		}

		static string GetName(MemberInfo member)
		{
			var displayNameAttribute = member
				.GetCustomAttribute<DisplayNameAttribute>();

			if (displayNameAttribute != null)
			{
				return displayNameAttribute.DisplayName;
			}
			else
			{
				var descriptionAttribute = member
					.GetCustomAttribute<DescriptionAttribute>();

				if (descriptionAttribute != null)
				{
					return descriptionAttribute.Description;
				}
			}

			return member.Name;
		}
	}
}
