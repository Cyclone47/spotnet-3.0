using System;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Spotnet.Extensions;

namespace Spotnet.Controls;

[DefaultProperty("Orientation")]
public class VirtualizingWrapPanel : VirtualizingPanel, IScrollInfo, IPanelKeyboardHelper
{
	public static readonly DependencyProperty ItemHeightProperty = DependencyProperty.Register("ItemHeight", typeof(double), typeof(VirtualizingWrapPanel), new PropertyMetadata(100.0, OnAppearancePropertyChanged));

	public static readonly DependencyProperty OrientationProperty = DependencyProperty.Register("Orientation", typeof(Orientation), typeof(VirtualizingWrapPanel), new PropertyMetadata(Orientation.Horizontal, OnAppearancePropertyChanged));

	public static readonly DependencyProperty ItemWidthProperty = DependencyProperty.Register("ItemWidth", typeof(double), typeof(VirtualizingWrapPanel), new PropertyMetadata(100.0, OnAppearancePropertyChanged));

	public static readonly DependencyProperty ScrollStepProperty = DependencyProperty.Register("ScrollStep", typeof(double), typeof(VirtualizingWrapPanel), new PropertyMetadata(10.0, OnAppearancePropertyChanged));

	private int itemsCount;

	private bool canHorizontallyScroll;

	private bool canVerticallyScroll;

	private Size contentExtent = new Size(0.0, 0.0);

	private Point contentOffset;

	private Size viewport = new Size(0.0, 0.0);

	private int previousItemCount;

	private const double ScrollPageSize = 0.8;

	public double ItemHeight
	{
		get
		{
			return (double)GetValue(ItemHeightProperty);
		}
		set
		{
			SetValue(ItemHeightProperty, value);
		}
	}

	public double ItemWidth
	{
		get
		{
			return (double)GetValue(ItemWidthProperty);
		}
		set
		{
			SetValue(ItemWidthProperty, value);
		}
	}

	public Orientation Orientation
	{
		get
		{
			return (Orientation)GetValue(OrientationProperty);
		}
		set
		{
			SetValue(OrientationProperty, value);
		}
	}

	public bool CanHorizontallyScroll
	{
		get
		{
			return canHorizontallyScroll;
		}
		set
		{
			if (canHorizontallyScroll != value)
			{
				canHorizontallyScroll = value;
				InvalidateMeasure();
			}
		}
	}

	public bool CanVerticallyScroll
	{
		get
		{
			return canVerticallyScroll;
		}
		set
		{
			if (canVerticallyScroll != value)
			{
				canVerticallyScroll = value;
				InvalidateMeasure();
			}
		}
	}

	public ScrollViewer ScrollOwner { get; set; }

	public double VerticalOffset => contentOffset.Y;

	public double ViewportHeight => viewport.Height;

	public double ViewportWidth => viewport.Width;

	public double ScrollStep
	{
		get
		{
			return (double)GetValue(ScrollStepProperty);
		}
		set
		{
			SetValue(ScrollStepProperty, value);
		}
	}

	public double ExtentHeight => contentExtent.Height;

	public double ExtentWidth => contentExtent.Width;

	public double HorizontalOffset => contentOffset.X;

	IPanelHelper IPanelKeyboardHelper.PanelHelper { get; set; }

	public void LineDown()
	{
		SetVerticalOffset(VerticalOffset + ScrollStep);
	}

	public void LineLeft()
	{
		SetHorizontalOffset(HorizontalOffset - ScrollStep);
	}

	public void LineRight()
	{
		SetHorizontalOffset(HorizontalOffset + ScrollStep);
	}

	public void LineUp()
	{
		SetVerticalOffset(VerticalOffset - ScrollStep);
	}

	public Rect MakeVisible(Visual visual, Rect rectangle)
	{
		MakeVisible(visual as UIElement);
		return rectangle;
	}

	public void MouseWheelDown()
	{
		SetVerticalOffset(VerticalOffset + ScrollStep);
	}

	public void MouseWheelLeft()
	{
		SetHorizontalOffset(HorizontalOffset - ScrollStep);
	}

	public void MouseWheelRight()
	{
		SetHorizontalOffset(HorizontalOffset + ScrollStep);
	}

	public void MouseWheelUp()
	{
		SetVerticalOffset(VerticalOffset - ScrollStep);
	}

	public void PageDown()
	{
		SetVerticalOffset(VerticalOffset + ViewportHeight * 0.8);
	}

	public void PageLeft()
	{
		SetHorizontalOffset(HorizontalOffset - ViewportHeight * 0.8);
	}

	public void PageRight()
	{
		SetHorizontalOffset(HorizontalOffset + ViewportHeight * 0.8);
	}

	public void PageUp()
	{
		SetVerticalOffset(VerticalOffset - viewport.Height * 0.8);
	}

	public void SetVerticalOffset(double offset)
	{
		if (offset < 0.0 || ViewportHeight >= ExtentHeight)
		{
			offset = 0.0;
		}
		else if (offset + ViewportHeight >= ExtentHeight)
		{
			offset = ExtentHeight - ViewportHeight;
		}
		contentOffset.Y = offset;
		if (ScrollOwner != null)
		{
			ScrollOwner.InvalidateScrollInfo();
		}
		InvalidateMeasure();
	}

	public void SetHorizontalOffset(double offset)
	{
		if (offset < 0.0 || ViewportWidth >= ExtentWidth)
		{
			offset = 0.0;
		}
		else if (offset + ViewportWidth >= ExtentWidth)
		{
			offset = ExtentWidth - ViewportWidth;
		}
		contentOffset.X = offset;
		if (ScrollOwner != null)
		{
			ScrollOwner.InvalidateScrollInfo();
		}
		InvalidateMeasure();
	}

	internal void PageLast()
	{
		contentOffset.Y = ExtentHeight;
		if (ScrollOwner != null)
		{
			ScrollOwner.InvalidateScrollInfo();
		}
		InvalidateMeasure();
	}

	internal void PageFirst()
	{
		contentOffset.Y = 0.0;
		if (ScrollOwner != null)
		{
			ScrollOwner.InvalidateScrollInfo();
		}
		InvalidateMeasure();
	}

	protected override void OnItemsChanged(object sender, ItemsChangedEventArgs args)
	{
		switch (args.Action)
		{
		case NotifyCollectionChangedAction.Remove:
		case NotifyCollectionChangedAction.Replace:
		case NotifyCollectionChangedAction.Move:
			RemoveInternalChildRange(args.Position.Index, args.ItemUICount);
			break;
		case NotifyCollectionChangedAction.Reset:
		{
			ItemsControl itemsOwner = ItemsControl.GetItemsOwner(this);
			if (itemsOwner == null)
			{
				break;
			}
			if (previousItemCount != itemsOwner.Items.Count)
			{
				if (Orientation == Orientation.Horizontal)
				{
					SetVerticalOffset(0.0);
				}
				else
				{
					SetHorizontalOffset(0.0);
				}
			}
			previousItemCount = itemsOwner.Items.Count;
			break;
		}
		}
	}

	protected override Size MeasureOverride(Size availableSize)
	{
		InvalidateScrollInfo(availableSize);
		int firstVisibleItemIndex;
		int lastVisibleItemIndex;
		if (Orientation == Orientation.Horizontal)
		{
			GetVerticalVisibleRange(out firstVisibleItemIndex, out lastVisibleItemIndex);
		}
		else
		{
			GetHorizontalVisibleRange(out firstVisibleItemIndex, out lastVisibleItemIndex);
		}
		UIElementCollection children = base.Children;
		IItemContainerGenerator itemContainerGenerator = base.ItemContainerGenerator;
		if (itemContainerGenerator != null)
		{
			GeneratorPosition position = itemContainerGenerator.GeneratorPositionFromIndex(firstVisibleItemIndex);
			int num = ((position.Offset == 0) ? position.Index : (position.Index + 1));
			if (num == -1)
			{
				RefreshOffset();
			}
			using (itemContainerGenerator.StartAt(position, GeneratorDirection.Forward, allowStartAtRealizedItem: true))
			{
				int num2 = firstVisibleItemIndex;
				while (num2 <= lastVisibleItemIndex)
				{
					bool isNewlyRealized;
					UIElement uIElement = itemContainerGenerator.GenerateNext(out isNewlyRealized) as UIElement;
					if (isNewlyRealized)
					{
						if (num >= children.Count)
						{
							AddInternalChild(uIElement);
						}
						else
						{
							InsertInternalChild(num, uIElement);
						}
						itemContainerGenerator.PrepareItemContainer(uIElement);
					}
					uIElement?.Measure(new Size(ItemWidth, ItemHeight));
					num2++;
					num++;
				}
			}
			CleanUpChildren(firstVisibleItemIndex, lastVisibleItemIndex);
		}
		Size size = availableSize;
		if (double.IsPositiveInfinity(availableSize.Width))
		{
			size = new Size(GetExtent(size, itemsCount).Width, size.Height);
		}
		if (double.IsPositiveInfinity(availableSize.Height))
		{
			size = new Size(size.Width, GetExtent(size, itemsCount).Height);
		}
		return size;
	}

	protected override Size ArrangeOverride(Size finalSize)
	{
		bool isHorizontal = Orientation == Orientation.Horizontal;
		InvalidateScrollInfo(finalSize);
		int num = 0;
		foreach (object child in base.Children)
		{
			ArrangeChild(isHorizontal, finalSize, num++, child as UIElement);
		}
		return finalSize;
	}

	private static void OnAppearancePropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
	{
		if (d is UIElement uIElement)
		{
			uIElement.InvalidateMeasure();
		}
	}

	private void MakeVisible(UIElement element)
	{
		ItemContainerGenerator itemContainerGeneratorForPanel = base.ItemContainerGenerator.GetItemContainerGeneratorForPanel(this);
		if (element == null || itemContainerGeneratorForPanel == null)
		{
			return;
		}
		for (int num = itemContainerGeneratorForPanel.IndexFromContainer(element); num == -1; num = itemContainerGeneratorForPanel.IndexFromContainer(element))
		{
			element = element.ParentOfType<UIElement>();
		}
		ScrollViewer scrollViewer = element.ParentOfType<ScrollViewer>();
		if (scrollViewer == null)
		{
			return;
		}
		Rect rect = element.TransformToVisual(scrollViewer).TransformBounds(new Rect(new Point(0.0, 0.0), element.RenderSize));
		if (Orientation == Orientation.Horizontal)
		{
			if (rect.Bottom > ViewportHeight)
			{
				SetVerticalOffset(contentOffset.Y + rect.Bottom - ViewportHeight);
			}
			else if (rect.Top < 0.0)
			{
				SetVerticalOffset(contentOffset.Y + rect.Top);
			}
		}
		else if (rect.Right > ViewportWidth)
		{
			SetHorizontalOffset(contentOffset.X + rect.Right - ViewportWidth);
		}
		else if (rect.Left < 0.0)
		{
			SetHorizontalOffset(contentOffset.X + rect.Left);
		}
	}

	public void GetVerticalVisibleRange(out int firstVisibleItemIndex, out int lastVisibleItemIndex)
	{
		int verticalChildrenCountPerRow = GetVerticalChildrenCountPerRow(contentExtent);
		firstVisibleItemIndex = (int)Math.Floor(VerticalOffset / ItemHeight) * verticalChildrenCountPerRow;
		if (double.IsInfinity(ViewportHeight))
		{
			lastVisibleItemIndex = itemsCount - 1;
		}
		else
		{
			lastVisibleItemIndex = (int)Math.Ceiling((VerticalOffset + ViewportHeight) / ItemHeight) * verticalChildrenCountPerRow - 1;
		}
		AdjustVisibleRange(ref firstVisibleItemIndex, ref lastVisibleItemIndex);
	}

	public void GetHorizontalVisibleRange(out int firstVisibleItemIndex, out int lastVisibleItemIndex)
	{
		int horizontalChildrenCountPerRow = GetHorizontalChildrenCountPerRow(contentExtent);
		firstVisibleItemIndex = (int)Math.Floor(HorizontalOffset / ItemWidth) * horizontalChildrenCountPerRow;
		if (double.IsInfinity(ViewportWidth))
		{
			lastVisibleItemIndex = itemsCount;
		}
		else
		{
			lastVisibleItemIndex = (int)Math.Ceiling((HorizontalOffset + ViewportWidth) / ItemWidth) * horizontalChildrenCountPerRow - 1;
		}
		AdjustVisibleRange(ref firstVisibleItemIndex, ref lastVisibleItemIndex);
	}

	private void AdjustVisibleRange(ref int firstVisibleItemIndex, ref int lastVisibleItemIndex)
	{
		firstVisibleItemIndex--;
		lastVisibleItemIndex++;
		ItemsControl itemsOwner = ItemsControl.GetItemsOwner(this);
		if (itemsOwner != null)
		{
			if (firstVisibleItemIndex < 0)
			{
				firstVisibleItemIndex = 0;
			}
			if (lastVisibleItemIndex >= itemsOwner.Items.Count)
			{
				lastVisibleItemIndex = itemsOwner.Items.Count - 1;
			}
		}
	}

	private void CleanUpChildren(int minIndex, int maxIndex)
	{
		UIElementCollection children = base.Children;
		IItemContainerGenerator itemContainerGenerator = base.ItemContainerGenerator;
		for (int num = children.Count - 1; num >= 0; num--)
		{
			GeneratorPosition position = new GeneratorPosition(num, 0);
			int num2 = itemContainerGenerator.IndexFromGeneratorPosition(position);
			if (num2 < minIndex || num2 > maxIndex)
			{
				itemContainerGenerator.Remove(position, 1);
				RemoveInternalChildRange(num, 1);
			}
		}
	}

	private void ArrangeChild(bool isHorizontal, Size finalSize, int index, UIElement child)
	{
		if (child != null)
		{
			int num = (isHorizontal ? GetVerticalChildrenCountPerRow(finalSize) : GetHorizontalChildrenCountPerRow(finalSize));
			int num2 = base.ItemContainerGenerator.IndexFromGeneratorPosition(new GeneratorPosition(index, 0));
			int num3 = (isHorizontal ? (num2 / num) : (num2 % num));
			int num4 = (isHorizontal ? (num2 % num) : (num2 / num));
			Rect finalRect = new Rect((double)num4 * ItemWidth, (double)num3 * ItemHeight, ItemWidth, ItemHeight);
			if (isHorizontal)
			{
				finalRect.Y -= VerticalOffset;
			}
			else
			{
				finalRect.X -= HorizontalOffset;
			}
			child.Arrange(finalRect);
		}
	}

	private void InvalidateScrollInfo(Size availableSize)
	{
		ItemsControl itemsOwner = ItemsControl.GetItemsOwner(this);
		if (itemsOwner != null)
		{
			itemsCount = itemsOwner.Items.Count;
			Size extent = GetExtent(availableSize, itemsCount);
			if (extent != contentExtent)
			{
				contentExtent = extent;
				RefreshOffset();
			}
			if (!double.IsPositiveInfinity(availableSize.Width) && !double.IsPositiveInfinity(availableSize.Height) && availableSize != viewport)
			{
				viewport = availableSize;
				InvalidateScrollOwner();
				RefreshOffset();
			}
		}
	}

	private void RefreshOffset()
	{
		if (Orientation == Orientation.Horizontal)
		{
			SetVerticalOffset(VerticalOffset);
		}
		else
		{
			SetHorizontalOffset(HorizontalOffset);
		}
	}

	private void InvalidateScrollOwner()
	{
		if (ScrollOwner != null)
		{
			ScrollOwner.InvalidateScrollInfo();
		}
	}

	private Size GetExtent(Size availableSize, int itemCount)
	{
		if (Orientation == Orientation.Horizontal)
		{
			int verticalChildrenCountPerRow = GetVerticalChildrenCountPerRow(availableSize);
			return new Size((double)verticalChildrenCountPerRow * ItemWidth, ItemHeight * Math.Ceiling((double)itemCount / (double)verticalChildrenCountPerRow));
		}
		int horizontalChildrenCountPerRow = GetHorizontalChildrenCountPerRow(availableSize);
		return new Size(ItemWidth * Math.Ceiling((double)itemCount / (double)horizontalChildrenCountPerRow), (double)horizontalChildrenCountPerRow * ItemHeight);
	}

	private int GetVerticalChildrenCountPerRow(Size availableSize)
	{
		int num = 0;
		if (availableSize.Width == double.PositiveInfinity)
		{
			return base.Children.Count;
		}
		return Math.Max(1, (int)Math.Floor(availableSize.Width / ItemWidth));
	}

	private int GetHorizontalChildrenCountPerRow(Size availableSize)
	{
		int num = 0;
		if (availableSize.Height == double.PositiveInfinity)
		{
			return base.Children.Count;
		}
		return Math.Max(1, (int)Math.Floor(availableSize.Height / ItemHeight));
	}

	Point IPanelKeyboardHelper.GetOffsets(int index)
	{
		FrameworkElement firstContainerInViewport = GetFirstContainerInViewport();
		FrameworkElement lastContainerInViewport = GetLastContainerInViewport();
		if (firstContainerInViewport != null && lastContainerInViewport != null)
		{
			int num = ((ItemContainerGenerator)base.ItemContainerGenerator).IndexFromContainer(firstContainerInViewport);
			int num2 = ((ItemContainerGenerator)base.ItemContainerGenerator).IndexFromContainer(lastContainerInViewport);
			if (index >= num && index <= num2)
			{
				return new Point(HorizontalOffset, VerticalOffset);
			}
		}
		int num3 = index / GetVerticalChildrenCountPerRow(viewport);
		double num4 = (double)num3 * ItemHeight;
		double num5 = (double)num3 * ItemWidth;
		Point result = new Point(num5, num4);
		if (num4 + ItemHeight > VerticalOffset + ViewportHeight)
		{
			result.Y = num4 - ViewportHeight + ItemHeight;
		}
		if (num4 + ItemWidth > HorizontalOffset + ViewportWidth)
		{
			result.X = num5 - ViewportWidth + ItemWidth;
		}
		return result;
	}

	int IPanelKeyboardHelper.GetPageUpIndex(int fromIndex)
	{
		FrameworkElement firstContainerInViewport = GetFirstContainerInViewport();
		FrameworkElement lastContainerInViewport = GetLastContainerInViewport();
		if (firstContainerInViewport != null && lastContainerInViewport != null)
		{
			int num = ((ItemContainerGenerator)base.ItemContainerGenerator).IndexFromContainer(firstContainerInViewport);
			((ItemContainerGenerator)base.ItemContainerGenerator).IndexFromContainer(lastContainerInViewport);
			if (num != fromIndex)
			{
				return num;
			}
		}
		int horizontalChildrenCountPerRow = GetHorizontalChildrenCountPerRow(viewport);
		int verticalChildrenCountPerRow = GetVerticalChildrenCountPerRow(viewport);
		int num2 = fromIndex - horizontalChildrenCountPerRow * verticalChildrenCountPerRow;
		if (num2 >= 0)
		{
			return num2;
		}
		return 0;
	}

	int IPanelKeyboardHelper.GetPageDownIndex(int fromIndex)
	{
		FrameworkElement firstContainerInViewport = GetFirstContainerInViewport();
		FrameworkElement lastContainerInViewport = GetLastContainerInViewport();
		if (firstContainerInViewport != null && lastContainerInViewport != null)
		{
			((ItemContainerGenerator)base.ItemContainerGenerator).IndexFromContainer(firstContainerInViewport);
			int num = ((ItemContainerGenerator)base.ItemContainerGenerator).IndexFromContainer(lastContainerInViewport);
			if (num != fromIndex)
			{
				return num;
			}
		}
		int horizontalChildrenCountPerRow = GetHorizontalChildrenCountPerRow(viewport);
		int verticalChildrenCountPerRow = GetVerticalChildrenCountPerRow(viewport);
		int num2 = fromIndex + horizontalChildrenCountPerRow * verticalChildrenCountPerRow;
		if (num2 <= itemsCount - 1)
		{
			return num2;
		}
		return itemsCount - 1;
	}

	double IPanelKeyboardHelper.GetVerticalOffsetForTouch()
	{
		return VerticalOffset;
	}

	double IPanelKeyboardHelper.GetHorizontalOffsetForTouch()
	{
		return HorizontalOffset;
	}

	private bool IsInTheViewport(FrameworkElement item)
	{
		if (item == null)
		{
			return false;
		}
		Rect layoutSlot = ((IPanelKeyboardHelper)this).PanelHelper.GetLayoutSlot(item);
		if (layoutSlot.Y >= 0.0 && layoutSlot.Height + layoutSlot.Y <= ViewportHeight && layoutSlot.X >= 0.0)
		{
			return layoutSlot.Width + layoutSlot.X <= ViewportWidth;
		}
		return false;
	}

	private FrameworkElement GetFirstContainerInViewport()
	{
		return base.Children.Cast<FrameworkElement>().FirstOrDefault((FrameworkElement item) => IsInTheViewport(item));
	}

	private FrameworkElement GetLastContainerInViewport()
	{
		return base.Children.Cast<FrameworkElement>().LastOrDefault((FrameworkElement item) => IsInTheViewport(item));
	}
}
