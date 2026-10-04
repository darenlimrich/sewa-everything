(function () {
    if (window.top !== window.self) {
        document.documentElement.style.display = 'none';

        try {
            window.top.location = window.self.location;
        } catch {
        }

        return;
    }

    const KUNCI = 'sewa-tema';
    const akar = document.documentElement;

    function sistem() {
        return window.matchMedia && window.matchMedia('(prefers-color-scheme: dark)').matches
            ? 'dark'
            : 'light';
    }

    function tersimpan() {
        try {
            return localStorage.getItem(KUNCI);
        } catch {
            return null;
        }
    }

    function pasang(tema) {
        akar.setAttribute('data-theme', tema);
        return tema;
    }

    pasang(tersimpan() === 'dark' || tersimpan() === 'light' ? tersimpan() : sistem());

    window.sewaTema = {
        sekarang: () => akar.getAttribute('data-theme') || 'light',
        pilih: (tema) => {
            const dipilih = tema === 'dark' ? 'dark' : 'light';

            try {
                localStorage.setItem(KUNCI, dipilih);
            } catch {
            }

            return pasang(dipilih);
        }
    };

    const halus = () => !window.matchMedia
        || !window.matchMedia('(prefers-reduced-motion: reduce)').matches;

    window.sewaTerlihat = () => document.visibilityState !== 'hidden';

    window.sewaScrollNavIntoView = () => {
        const el = document.querySelector('.seller-nav__item.active');
        if (el) el.scrollIntoView({
            inline: 'center',
            block: 'nearest',
            behavior: halus() ? 'smooth' : 'auto'
        });
    };

    window.sewaScrollToPageTop = () => {
        const el = document.querySelector('[data-page-top]');

        if (!el) {
            window.scrollTo({ top: 0 });
            return;
        }

        const header = document.querySelector('.app-header');
        const tinggi = header ? header.getBoundingClientRect().height : 0;
        const atas = window.scrollY + el.getBoundingClientRect().top - tinggi - 16;

        window.scrollTo({ top: Math.max(0, atas) });
    };
    window.sewaRupiah = (el) => {
        if (!el) {
            return '';
        }

        const mentah = el.value;
        const karet = el.selectionStart === null || el.selectionStart === undefined
            ? mentah.length
            : el.selectionStart;

        let sebelum = 0;
        for (let i = 0; i < karet && i < mentah.length; i++) {
            if (mentah[i] >= '0' && mentah[i] <= '9') {
                sebelum++;
            }
        }

        const angka = mentah.replace(/\D/g, '').replace(/^0+(?=\d)/, '').slice(0, 12);
        const teks = angka === '' ? '' : Number(angka).toLocaleString('id-ID');

        el.value = teks;

        let posisi = 0;
        let hitung = 0;
        while (posisi < teks.length && hitung < sebelum) {
            if (teks[posisi] >= '0' && teks[posisi] <= '9') {
                hitung++;
            }
            posisi++;
        }

        try {
            el.setSelectionRange(posisi, posisi);
        } catch (e) {
        }

        return angka;
    };
})();
