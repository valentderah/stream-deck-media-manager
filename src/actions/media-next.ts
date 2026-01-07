import { action, KeyDownEvent, SingletonAction, WillAppearEvent } from '@elgato/streamdeck';
import { nextMedia } from '../utils/media-manager';

@action({ UUID: 'ru.valentderah.media-manager.media-next' })
export class MediaNextAction extends SingletonAction {
	override async onKeyDown(ev: KeyDownEvent): Promise<void> {
		await nextMedia();
	}
}

