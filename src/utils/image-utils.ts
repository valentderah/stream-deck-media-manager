import { IMAGE_SIZE_SINGLE_CELL, PLACEHOLDER_COLOR } from './constants';

export function generatePlaceholderImage(size: number = IMAGE_SIZE_SINGLE_CELL): string {
	const svg = `<svg width="${size}" height="${size}" xmlns="http://www.w3.org/2000/svg">
		<rect width="${size}" height="${size}" fill="${PLACEHOLDER_COLOR}"/>
	</svg>`;
	const base64 = Buffer.from(svg).toString('base64');
	return `data:image/svg+xml;base64,${base64}`;
}
