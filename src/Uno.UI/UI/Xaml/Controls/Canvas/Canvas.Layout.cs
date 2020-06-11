﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Windows.Foundation;
using Uno.Extensions;
using Microsoft.Extensions.Logging;
using Uno.Logging;

#if __WASM__
using _View = Windows.UI.Xaml.UIElement;
#elif NET461
using _View = Windows.UI.Xaml.UIElement;
#else
using _View = Windows.UI.Xaml.DependencyObject;
#endif


namespace Windows.UI.Xaml.Controls
{
	public partial class Canvas : ICustomClippingElement
	{
		protected override Size MeasureOverride(Size availableSize)
		{
			MeasureOverridePartial();
			// A canvas does not have dimensions and will always return zero even with a chidren collection.
			foreach (var child in Children)
			{
				if (child is _View)
				{
					MeasureElement(child, new Size(double.PositiveInfinity, double.PositiveInfinity));
				}
			}
			return new Size(0, 0);
		}

		partial void MeasureOverridePartial();

		protected override Size ArrangeOverride(Size finalSize)
		{
			foreach (var child in Children)
			{
				if (child is _View childView)
				{
					var desiredSize = GetElementDesiredSize(child);

					var childRect = new Rect
					{
						X = GetLeft(childView),
						Y = GetTop(childView),
						Width = desiredSize.Width,
						Height = desiredSize.Height,
					};

#if __IOS__
					child.Layer.ZPosition = (nfloat)GetZIndex(childView);
#endif

					ArrangeElement(child, childRect);
				}
			}

			return finalSize;
		}

		bool ICustomClippingElement.AllowClippingToLayoutSlot => false;
		bool ICustomClippingElement.ForceClippingToLayoutSlot => false;
	}
}
