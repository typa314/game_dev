using System;

namespace Game.Core.UI
{
    /// <summary>
    /// 表現層 View 基礎介面。
    /// </summary>
    public interface IView
    {
        bool IsVisible { get; set; }
    }

    /// <summary>
    /// Presenter 基礎介面。
    /// </summary>
    public interface IPresenter
    {
        void Initialize();
        void Dispose();
    }

    /// <summary>
    /// 抽象 Presenter 基底。
    /// </summary>
    public abstract class PresenterBase<TView> : IPresenter where TView : class, IView
    {
        protected TView View { get; private set; }

        public void AttachView(TView view)
        {
            View = view ?? throw new ArgumentNullException(nameof(view));
            OnViewAttached();
        }

        public void DetachView()
        {
            OnViewDetached();
            View = null;
        }

        public virtual void Initialize() { }
        public virtual void Dispose() { DetachView(); }

        protected virtual void OnViewAttached() { }
        protected virtual void OnViewDetached() { }
    }

    /// <summary>
    /// MVP 畫面基底類別，實作 IScreen 生命週期。
    /// </summary>
    public abstract class ScreenBase<TView, TPresenter> : IScreen
        where TView : class, IView
        where TPresenter : PresenterBase<TView>
    {
        public abstract string ScreenId { get; }
        public TPresenter Presenter { get; }
        public TView View { get; }

        protected ScreenBase(TView view, TPresenter presenter)
        {
            View = view ?? throw new ArgumentNullException(nameof(view));
            Presenter = presenter ?? throw new ArgumentNullException(nameof(presenter));
            Presenter.AttachView(View);
        }

        public virtual void OnPush(object args)
        {
            View.IsVisible = true;
            Presenter.Initialize();
        }

        public virtual void OnPop()
        {
            View.IsVisible = false;
            Presenter.Dispose();
        }

        public virtual void OnPause()
        {
            View.IsVisible = false;
        }

        public virtual void OnResume()
        {
            View.IsVisible = true;
        }

        public virtual bool OnBackRequested()
        {
            return false;
        }
    }
}
