(() => {
  const root = document.querySelector('.opencrm-landing');
  if (!root) return;
  const brandMeta = document.querySelector('meta[name="project-brand"]');
  const brandName = brandMeta?.content?.trim() ?? '';
  const reducedMotion = matchMedia('(prefers-reduced-motion: reduce)');
  const backgrounds = [...root.querySelectorAll('.backdrop>img')];
  const opening = root.querySelector('.opening');
  let activeBackground = 0;
  let visible = true;
  let timer;

  function scheduleBackground() {
    clearTimeout(timer);
    if (!visible || document.hidden || reducedMotion.matches) return;
    timer = setTimeout(() => {
      backgrounds[activeBackground].classList.remove('is-active');
      activeBackground = (activeBackground + 1) % backgrounds.length;
      backgrounds[activeBackground].classList.add('is-active');
      scheduleBackground();
    }, 5200);
  }

  new IntersectionObserver(entries => { visible = entries[0].isIntersecting; scheduleBackground(); }).observe(opening);
  document.addEventListener('visibilitychange', scheduleBackground);
  reducedMotion.addEventListener('change', scheduleBackground);
  scheduleBackground();

  const screens = {
    workday: { name:'Рабочий день', title:'От записи до выдачи', description:'Запланированные заказы, текущие работы, готовые и выданные автомобили.' },
    posts: { name:'Заказы по постам', title:'Загрузка каждого поста', description:'Автомобили, услуги и исполнители сгруппированы по рабочим постам.' },
    scheduler: { name:'Запись клиентов', title:'Расписание по постам', description:'Автомобили и время записи на выбранный день.' },
    order: { name:'Карточка заказа', title:'Всё по одному заказу', description:'Услуги, исполнитель, ход работ и расчёт с клиентом.' },
    materials: { name:'Склад и материалы', title:'Остатки и движения', description:'Количество материала, себестоимость и история списаний.' },
    finance: { name:'Финансы и оплаты', title:'Оплаты, расходы и долги', description:'Выручка, расходы, оплаты и задолженность по заказам.' }
  };
  const image = root.querySelector('.product-screen');
  const imageLink = root.querySelector('.screen-link');
  const controls = [...root.querySelectorAll('[data-screen]')];
  controls.forEach(control => { screens[control.dataset.screen].file = control.href; });
  let current = 'workday';
  function selectScreen(id) {
    current = id;
    const screen = screens[id];
    image.src = screen.file;
    image.alt = brandName ? screen.name + ' в ' + brandName : screen.name;
    imageLink.href = screen.file;
    root.querySelector('[data-description-title]').textContent = screen.title;
    root.querySelector('[data-description]').textContent = screen.description;
    controls.forEach(control => {
      if (control.dataset.screen === id) control.setAttribute('aria-current','true');
      else control.removeAttribute('aria-current');
    });
  }
  controls.forEach(control => control.addEventListener('click', event => { event.preventDefault(); selectScreen(control.dataset.screen); }));

  const viewer = root.querySelector('.viewer');
  function openScreen(id) {
    const screen = screens[id];
    const fullImage = viewer.querySelector('img');
    fullImage.src = screen.file;
    fullImage.alt = brandName ? screen.name + ' в ' + brandName : screen.name;
    viewer.querySelector('[data-viewer-title]').textContent = screen.name;
    viewer.showModal();
  }
  imageLink.addEventListener('click', event => {
    if (event.ctrlKey || event.metaKey || event.altKey || event.shiftKey) return;
    event.preventDefault();
    openScreen(current);
  });
  root.querySelectorAll('[data-story-open]').forEach(link => link.addEventListener('click', event => {
    if (event.ctrlKey || event.metaKey || event.altKey || event.shiftKey) return;
    event.preventDefault();
    openScreen(link.dataset.storyOpen);
  }));
  root.querySelector('.viewer-close').addEventListener('click', () => viewer.close());
  viewer.addEventListener('click', event => { if (event.target === viewer) viewer.close(); });

  root.querySelectorAll('[data-copy-promo]').forEach(button => button.addEventListener('click', async () => {
    const label = button.querySelector('[data-copy-label]');
    const code = button.querySelector('code').textContent.trim();
    try {
      await navigator.clipboard.writeText(code);
      label.textContent = 'Скопировано';
      button.setAttribute('aria-label', `Промокод ${code} скопирован`);
    } catch {
      label.textContent = 'Выделите код';
      button.setAttribute('aria-label', `Выделите и скопируйте промокод ${code}`);
    }
  }));

  const story = root.querySelector('.story');
  const storySteps = [...story.querySelectorAll('[data-story-step]')];
  const storyShots = [...story.querySelectorAll('[data-story-shot]')];
  const storyLink = story.querySelector('.story-screen-link');
  let activeStory = -1;
  function renderStory() {
    const bounds = story.getBoundingClientRect();
    if (bounds.bottom <= 0 || bounds.top >= innerHeight) return;
    let nearest = 0;
    let distance = Infinity;
    storySteps.forEach((step, index) => {
      const box = step.getBoundingClientRect();
      const nextDistance = Math.abs(box.top + box.height / 2 - innerHeight / 2);
      if (nextDistance < distance) { nearest = index; distance = nextDistance; }
    });
    if (nearest === activeStory) return;
    activeStory = nearest;
    const id = storySteps[nearest].dataset.storyStep;
    storySteps.forEach((step, index) => step.classList.toggle('is-active', index === nearest));
    storyShots.forEach(shot => {
      const active = shot.dataset.storyShot === id;
      shot.classList.toggle('is-active', active);
      shot.setAttribute('aria-hidden', String(!active));
    });
    storyLink.href = screens[id].file;
    storyLink.dataset.storyOpen = id;
    story.querySelector('[data-story-caption]').textContent = screens[id].name;
  }

  const media = root.querySelector('.product-media');
  let queued = false;
  function renderScroll() {
    queued = false;
    const heroProgress = Math.max(0,Math.min(1,-opening.getBoundingClientRect().top / opening.offsetHeight));
    opening.style.setProperty('--parallax', reducedMotion.matches ? '0px' : (heroProgress * 45).toFixed(1) + 'px');
    const progress = Math.max(0,Math.min(1,(innerHeight - media.getBoundingClientRect().top) / (innerHeight * .65)));
    media.style.setProperty('--lift', reducedMotion.matches ? '0px' : ((1 - progress) * 24).toFixed(1) + 'px');
    renderStory();
  }
  function queueScroll() { if (queued) return; queued = true; requestAnimationFrame(renderScroll); }
  addEventListener('scroll',queueScroll,{passive:true});
  addEventListener('resize',queueScroll,{passive:true});
  reducedMotion.addEventListener('change',queueScroll);
  renderScroll();
})();
