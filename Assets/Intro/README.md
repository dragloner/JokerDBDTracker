# Пси-вступление

`psi-joker-zone.png` создан встроенным инструментом imagegen. Обе фигуры используют фотографии Джокера, предоставленные пользователем: `Assets/Brand/portrait.png` и `Assets/Brand/smile.png`. Это авторская интерпретация атмосферы Зоны, а не извлечённые игровые модели.

Финальный промпт редактирования:

> Edit target image 1 (Zone background). Images 2 and 3 are identity references of the SAME man, Joker, with a rounded face, short dark hair, light eyes and characteristic expression. USER REQUIREMENT: EVERY humanoid/monster in the scene must be a recognizable version of Joker from the face references. Replace the tall Controller at far right with Joker as a psychic Controller: same recognizable face and facial proportions, hood framing face, subtle psi mutations and eerie green rim light, not a generic gaunt monster face. Replace the crouching creature lower right with another Joker psi mutant: his face remains clearly recognizable, with subtle tentacles AROUND chin not covering face, pale olive skin and mutated body. Any distant humanoid silhouettes, if any, also must be Joker variants. Preserve the original abandoned radar lab, mist, anomaly lightning, detailed cinematic environment and 16:9 composition. Left HALF remains dark empty space for UI typography, monsters on right THIRD. Premium photorealistic game key art, charcoal and desaturated olive with restrained green and amber accents. No text, no logos, no UI, no gore. Show recognizable expressive faces, not masks hiding the identity.

`psi-ambience.wav`, `psi-wake.wav` и `psi-enter.wav` переработаны из существующего `Assets/Sounds/psi.mp3`: стерео PCM, 48 кГц, 16 бит. Исходный звук плеера не изменяется. Фон сочетает узнаваемый исходный слой, замедленный низкий слой и тихие стереоотражения. Переходы между фрагментами сглажены; резкие высокие частоты и инфранизкие составляющие приглушены. Начало и окончание плавные, без щелчков. Все три файла управляются только вступлением.

Воспроизводимый рендер: `python tools/render_intro_audio.py artifacts/psi-source.wav` (Python + NumPy). Вход — декодированный из исходного MP3 стерео PCM16 WAV, 48 кГц. Декодирование не должно нормализовать или менять исходный материал.

Текстовые пси-фразы остаются на экране, голосового диктора нет. Озвучка Windows SAPI полностью удалена из вступления.
